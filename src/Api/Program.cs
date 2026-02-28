using System.IO.Compression;
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
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using NetCommerce.Kernel.Wolverine;
using NetCommerce.Kernel.Security;
using NetCommerce.Kernel.Security.Authentication;
using NetCommerce.Kernel.AspNetCore;
using Wolverine;
using Wolverine.Runtime;
using NetCommerce.Kernel.EfCore.Persistence;
using Wolverine.Http;
using Wolverine.SignalR;
using JasperFx.CodeGeneration;
using Oakton;

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
builder.Host.UseWolverineMessaging(
    builder.Configuration,
    opts =>
    {
        // ============================================================================
        // NATIVE AOT CONFIGURATION (Phase 4)
        // ============================================================================

        // 1. Tell Wolverine: "Use ONLY pre-generated types - fail if missing"
        // "Static" means: Strictly require source-generated code. No runtime fallback.
        // This ensures AOT compliance and prevents silent runtime failures.
        opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;

        // 2. Tell Wolverine where to write the generated code
        // This allows us to inspect it and commit it to source control if needed.
        opts.CodeGeneration.GeneratedCodeOutputPath =
            Path.Combine(Directory.GetCurrentDirectory(), "Internal", "Generated");

        // 3. Ensure DbContext integration is configured
        // (This matches the configuration below but is applied to Wolverine's internal pipeline)
        opts.ConfigureKernelDefaults<BaseDbContext>();
    },
    // Handler discovery assemblies (all modules)
    typeof(CreateProductCommand),          // Catalog
    typeof(ReserveStockCommand),           // Inventory
    typeof(CreateOrderCommand),            // Ordering
    typeof(RefundPaymentTransactionCommand), // Payments
    typeof(CheckDailyReconciliation)       // Finance
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
// Aspire Default Endpoints (Health checks)
// ============================================================================
app.MapDefaultEndpoints();

// ============================================================================
// Resilient Database Migrations (Dev or AutoMigrate flag)
// ============================================================================
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("AutoMigrate"))
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
