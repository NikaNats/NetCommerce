using NetCommerce.Api.Extensions;
using NetCommerce.Api.Extensions.Hosting;
using NetCommerce.Api.Middleware;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.Kernel.Wolverine;
using Wolverine;
using Wolverine.Http;

// ============================================================================
// CRITICAL: Npgsql 6.0+ Strict UTC Enforcement
// ============================================================================
// Disable legacy timestamp behavior to enforce DateTimeKind.Utc for all PostgreSQL timestamp with time zone columns.
// Without this, Npgsql will throw exceptions if DateTime.Kind is Local or Unspecified.
// This ensures all DateTime values in the application are UTC-compliant.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);

var builder = WebApplication.CreateBuilder(args);

// Detects test/dev runs (including xUnit/testhost runners) so Wolverine uses dynamic
// compilation there while enforcing strict static codegen in production. The assembly
// scan prevents Oakton hijacking ApplicationAssembly under WebApplicationFactory.
var isTestOrDev = builder.Environment.IsDevelopment()
    || builder.Environment.IsEnvironment("Testing")
    || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true
                                                     || a.GetName().Name?.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) == true);

// 1. Platform, security, and infrastructure (sliced into Extensions/Hosting/)
builder.AddServiceDefaults();
builder.AddEnterpriseSecurity();
builder.AddEnterpriseStorage();

// 2. Messaging (sliced into Extensions/Hosting/MessagingExtensions.cs)
builder.Services.AddSignalR();
builder.Host.AddEnterpriseWolverine(builder.Configuration, isTestOrDev);
builder.Services.Configure<WolverineOptions>(opts => opts.ConfigureKernelDefaults<BaseDbContext>());
builder.Services.AddWolverineHttp();

// 3. Problem Details for consistent error responses (RFC 9457)
builder.Services.AddKernelAspNetCore(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// 4. API services, AOT JSON, modules, versioning/OpenAPI
builder.Services.AddApiServicesMinimal(builder.Configuration);
builder.Services.AddAotJsonSerialization();
builder.Services.AddModules(builder.Configuration);
builder.Services.AddVersioning();
builder.AddNetCommerceOpenApi();

var app = builder.Build();

// 5. Dedicated CLI migration execution (returns exit code when --migrate-only is passed)
if (await app.RunMigrationsCliAsync(args) is int migrationExitCode)
    return migrationExitCode;

await app.EnsureMigratedForDevAsync(args);

// 6. Middleware pipeline (order is the contract — see PipelineExtensions) + endpoint maps
app.UseEnterprisePipeline();
app.MapEnterpriseEndpoints();

// Host Execution: Standard Web Server vs Oakton CLI Runner.
// When running in a test host (e.g. WebApplicationFactory) without CLI arguments,
// start the web host directly. This avoids Oakton overriding ApplicationAssembly to testhost.
// When CLI arguments are present (e.g. 'codegen write'), delegate to JasperFx command execution.
if (args.Length == 0 && isTestOrDev)
{
    await app.RunAsync();
    return 0;
}

// IL2026/IL3050: Oakton dispatches CLI *commands* via reflection, but the production container
// starts with empty args (default host run — no command dispatch). Codegen commands ('codegen
// write') execute exclusively in the JIT Dockerfile step and dev machines, never in the AOT binary.
// Passing codegen args to a trimmed binary fail-closes instead of silently misbehaving.
#pragma warning disable IL2026 // RequiresUnreferencedCode: prod path runs default host, not reflective dispatch
#pragma warning disable IL3050 // RequiresDynamicCode: same justification as IL2026 above
return await JasperFx.CommandLineHostingExtensions.RunJasperFxCommands(app, args);
#pragma warning restore IL3050
#pragma warning restore IL2026
