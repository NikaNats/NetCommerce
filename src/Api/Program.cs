using JasperFx.CodeGeneration;
using JasperFx.MultiTenancy;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Hybrid;
using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Application.Commands;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Kernel.AspNetCore;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.Kernel.Security;
using NetCommerce.Kernel.Security.Authentication;
using NetCommerce.Kernel.Wolverine;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Sagas;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using Oakton;
using System.IO.Compression;
using Wolverine;
using Wolverine.Http;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.Runtime;
using Wolverine.SignalR;

// ============================================================================
// CRITICAL: Npgsql 6.0+ Strict UTC Enforcement
// ============================================================================
// Disable legacy timestamp behavior to enforce DateTimeKind.Utc for all PostgreSQL timestamp with time zone columns.
// Without this, Npgsql will throw exceptions if DateTime.Kind is Local or Unspecified.
// This ensures all DateTime values in the application are UTC-compliant.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Add Aspire Service Defaults (OpenTelemetry, Health Checks, Service Discovery, Polly)
// ============================================================================
builder.AddServiceDefaults();

// ============================================================================
// Reverse Proxy Awareness (Forwarded Headers) - MUST be before RateLimiter/Auth
// ============================================================================
// CRITICAL SECURITY: Trust only KnownNetworks/KnownProxies. Never trust X-Forwarded-For from open internet.
// Behind ALB/Nginx/Cloudflare, RemoteIpAddress without this collapses all clients to proxy IP.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    // Default private ranges – override via configuration for production VPC CIDRs
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

// ============================================================================
// Enterprise HTTP Security Headers (OWASP Compliant)
// ============================================================================
builder.Services.AddNetCommerceSecurityHeaders();

// ============================================================================
// Enterprise-Hardened Web Host (Kestrel Security & Performance)
// ============================================================================
builder.AddEnterpriseWebHost();

// ============================================================================
// Defense in Depth: Antiforgery Protection
// ============================================================================
builder.Services.AddAntiforgery();

// ============================================================================
// SignalR (Required by Wolverine.SignalR)
// ============================================================================
builder.Services.AddSignalR();

// ============================================================================
// Wolverine Message Bus with Transactional Outbox
// Replaces MediatR with durable, at-least-once message delivery
// ============================================================================
var connectionString = builder.Configuration.GetConnectionString("OrderingDb")
                    ?? builder.Configuration.GetConnectionString("postgres");
builder.Host.UseWolverineMessaging(
    builder.Configuration,
    opts =>
    {
        opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
        opts.CodeGeneration.GeneratedCodeOutputPath =
            Path.Combine(Directory.GetCurrentDirectory(), "Internal", "Generated");

        if (!string.IsNullOrEmpty(connectionString))
        {
            opts.PersistMessagesWithPostgresql(connectionString, "wolverine");
        }

        opts.AddSagaType<OrderFulfillmentSaga>();
        opts.ConfigureKernelDefaults<BaseDbContext>();
    },
    typeof(CreateProductCommand),
    typeof(ReserveStockCommand),
    typeof(CreateOrderCommand),
    typeof(RefundPaymentTransactionCommand),
    typeof(CheckDailyReconciliation)
);

// Configure Wolverine options
builder.Services.Configure<WolverineOptions>(opts => opts.ConfigureKernelDefaults<BaseDbContext>());

// Add Wolverine.Http services for HTTP endpoints (required for MapWolverineEndpoints)
builder.Services.AddWolverineHttp();

// ============================================================================
// Problem Details for consistent error responses (RFC 9457)
// Configurable URIs for dev/prod environments
// ============================================================================
builder.Services.AddKernelAspNetCore(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ============================================================================
// Aspire-managed services - automatically configured via AppHost references
// ============================================================================

#pragma warning disable EXTEXP0018 // HybridCache is still evolving in .NET 10
// 1. Add HybridCache to the DI container
builder.Services.AddHybridCache(options =>
{
    // 2025 Best Practice: Set global defaults
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(60),
        LocalCacheExpiration = TimeSpan.FromMinutes(5) // L1 (RAM) is shorter for consistency
    };
});
#pragma warning restore EXTEXP0018

// Redis (Aspire will inject the connection string)
builder.AddRedisClient("redis");

// Azure Blob Storage (Aspire will inject the connection string)
builder.AddAzureBlobServiceClient("blobs");

// Seq for structured logging (Aspire will configure OTLP endpoint)
// Make Seq optional - if ServerUrl is not configured (e.g., in tests), skip it
var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
if (!string.IsNullOrEmpty(seqServerUrl))
{
    builder.AddSeqEndpoint("seq");
}

// Meilisearch for product search (read model)
builder.AddMeilisearchClient("meilisearch");

// ============================================================================
// Zero-Trust Authentication with Keycloak (Identity Mesh)
// ============================================================================
// This replaces the previous manual JWT configuration with a standardized
// Zero-Trust security stack that includes:
// - JWT Bearer authentication with strict validation
// - Keycloak role claim transformation (flattens nested JSON roles)
// - Optional token introspection for instant revocation (kill switch)
// - Token exchange support for secure downstream service calls
builder.AddZeroTrustAuthentication();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("admin"))
    .AddPolicy("VendorOnly", policy => policy.RequireRole("admin", "vendor"))
    .AddPolicy("CustomerOnly", policy => policy.RequireRole("customer"))
    .AddPolicy("OwnerOnly", policy =>
        policy.Requirements.Add(new NetCommerce.Kernel.Security.Authorization.ResourceOwnerRequirement()))
    .AddPolicy("AdminElevated", policy =>
    {
        policy.RequireRole("admin", "Admin");
        policy.Requirements.Add(new NetCommerce.Kernel.Security.Authorization.AdminElevatedRequirement());
    });

// ============================================================================
// Response Compression (Brotli + Gzip)
// ============================================================================
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/problem+json"]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.SmallestSize;
});

// ============================================================================
// API Services (without controllers)
// ============================================================================
builder.Services.AddApiServicesMinimal(builder.Configuration);

// ============================================================================
// JSON Source Generation for Native AOT (Phase 6 Hardened)
// ============================================================================
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // CRITICAL: Enforce strict Source Generation - no reflection fallback.
    // This ensures all types are pre-compiled for Native AOT.
    // If a type is missing from ApiJsonContext, it will fail fast in Dev.
    options.SerializerOptions.TypeInfoResolverChain.Clear();
    options.SerializerOptions.TypeInfoResolverChain.Add(NetCommerce.Api.Serialization.ApiJsonContext.Default);

    // Custom converters for Value Objects
    options.SerializerOptions.Converters.Add(new NetCommerce.Kernel.Core.Serialization.StronglyTypedIdJsonConverterFactory());
});

// ============================================================================
// Module Registration
// ============================================================================
builder.Services.AddModules(builder.Configuration);

// ============================================================================
// OpenAPI / Swagger
// ============================================================================
builder.Services.AddVersioning();
builder.AddNetCommerceOpenApi();

var app = builder.Build();

// ============================================================================
// CI/CD Migration Runner Mode (--migrate-only)
// ============================================================================
// EF Core migrations require dynamic code (IL3050) and therefore cannot run
// inside the Native AOT production container. Deployment pipelines execute the
// JIT-built binary with this flag as a dedicated pre-deployment step; the
// process exits cleanly once all bounded-context schemas are applied.
if (args.Contains("--migrate-only"))
{
    Console.WriteLine("[MIGRATION RUNNER] Running PostgreSQL schema migrations across all bounded contexts...");

#pragma warning disable IL3050 // EF Core migrations require dynamic code - this is a JIT pipeline step, never the AOT container
    await app.Services.ApplyMigrationsAsync<CatalogDbContext>();
    await app.Services.ApplyMigrationsAsync<OrderingDbContext>();
    await app.Services.ApplyMigrationsAsync<InventoryDbContext>();
    await app.Services.ApplyMigrationsAsync<PaymentsDbContext>();
    await app.Services.ApplyMigrationsAsync<FinanceDbContext>();
    await app.Services.ApplyMigrationsAsync<ShippingDbContext>();
#pragma warning restore IL3050

    Console.WriteLine("[MIGRATION RUNNER] Schema migrations complete. Exiting clean.");
    return 0;
}

// ============================================================================
// Forwarded Headers (MUST be before RateLimiter, SecurityHeaders, Auth)
// Resolves RemoteIpAddress behind ALB/Nginx/Cloudflare; prevents IP spoofing DoS.
// ============================================================================
app.UseForwardedHeaders();

// ============================================================================
// CRITICAL: Security Headers Middleware (MUST BE FIRST after ForwardedHeaders)
// ============================================================================
app.UseNetCommerceSecurityHeaders();

// ============================================================================
// Aspire Default Endpoints (Health checks)
// ============================================================================
app.MapDefaultEndpoints();

// ============================================================================
// Resilient Database Migrations (Dev or AutoMigrate flag)
// ============================================================================
// Skipped when running a JasperFx CLI command (e.g. 'codegen write') - the app
// is not serving and must not require a live database. --migrate-only performs
// its own dedicated migration pass below.
if ((app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("AutoMigrate"))
    && !args.Contains("--migrate-only")
    && !args.Contains("codegen"))
{
#pragma warning disable IL3050 // EF Core migrations require dynamic code - this is dev-only
    await app.Services.ApplyMigrationsAsync<CatalogDbContext>();
    await app.Services.ApplyMigrationsAsync<OrderingDbContext>();
    await app.Services.ApplyMigrationsAsync<InventoryDbContext>();
    await app.Services.ApplyMigrationsAsync<PaymentsDbContext>();
    await app.Services.ApplyMigrationsAsync<FinanceDbContext>();
    await app.Services.ApplyMigrationsAsync<ShippingDbContext>();
#pragma warning restore IL3050
}

// ============================================================================
// Middleware Pipeline
// ============================================================================
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseResponseCompression();

app.UseMiddleware<CorrelationIdMiddleware>();
// Note: IdempotencyMiddleware removed from global pipeline.
// Idempotency is now applied selectively to mutation endpoints via .WithIdempotency() filter.
// This prevents memory overhead from response buffering on read operations.

if (app.Environment.IsDevelopment()) app.UseNetCommerceOpenApi();

// ============================================================================
// Enterprise-Hardened Web Host Middleware
// ============================================================================
app.UseEnterpriseWebHost();

app.UseHttpsRedirection();
app.UseRateLimiter(); // Rate limiting before CORS/Auth - prevents DoS
app.UseCors("AllowConfigured");
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// Zero-Trust Middleware (Token Introspection / Kill Switch)
// ============================================================================
// When enabled (Auth__IntrospectionEnabled=true), this middleware validates
// every token against Keycloak's introspection endpoint. If a user is banned
// or their token is revoked, they are blocked immediately - not when JWT expires.
app.UseZeroTrustMiddleware();

// ============================================================================
// SignalR Hub for Real-Time Order Notifications
// ============================================================================
// Wolverine's built-in WolverineHub provides WebSocket messaging to browsers.
// Frontend connects to this endpoint to receive order status updates.
app.MapWolverineSignalRHub("/api/messages");

// ============================================================================
// Defense in Depth: Antiforgery Middleware
// ============================================================================
app.UseAntiforgery();

// ============================================================================
// Map Minimal API Endpoints
// ============================================================================
var versionSet = app.GetDefaultApiVersionSet();

// PHASE 6: Explicit endpoint registration for Native AOT compatibility.
// Eliminates reflection-based assembly scanning (Assembly.GetTypes() / Activator.CreateInstance).
app.MapNetCommerceEndpoints(versionSet);


// ============================================================================
// Wolverine.Http Endpoints (Zero-Ceremony, Attribute-Based)
// ============================================================================
// Maps endpoints decorated with [WolverineGet], [WolverinePost], etc.
// These endpoints benefit from Wolverine's compound handler pattern,
// automatic cascading messages, and transactional outbox integration.
app.MapWolverineEndpoints();

// ============================================================================
// NATIVE AOT: Use Oakton Commands for CLI Support
// Enables: dotnet run -- codegen write
// This generates static handler code that the AOT compiler can see.
// ============================================================================
return await JasperFx.CommandLineHostingExtensions.RunJasperFxCommands(app, args);
