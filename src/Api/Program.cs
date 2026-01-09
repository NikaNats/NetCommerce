using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Hybrid;
using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.SharedKernel.Versioning;
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
using NetCommerce.Kernel.Wolverine;
using NetCommerce.Kernel.Security;
using NetCommerce.SharedKernel.Infrastructure.Kestrel;
using Wolverine;
using NetCommerce.Kernel.EfCore.Persistence;
using NetCommerce.SharedKernel.Infrastructure.Messaging;
using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;
using Wolverine.SignalR;

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
// Wolverine Message Bus with Transactional Outbox
// Replaces MediatR with durable, at-least-once message delivery
// ============================================================================
builder.Host.UseWolverineMessaging(
    builder.Configuration,
    // Handler discovery assemblies (all modules)
    typeof(CreateProductCommand),          // Catalog
    typeof(ReserveStockCommand),           // Inventory
    typeof(CreateOrderCommand),            // Ordering
    typeof(RefundPaymentTransactionCommand), // Payments
    typeof(CheckDailyReconciliation)       // Finance
);

// Configure Wolverine options
builder.Services.Configure<WolverineOptions>(opts => opts.ConfigureKernelDefaults<BaseDbContext>());

// ============================================================================
// Problem Details for consistent error responses
// ============================================================================
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
    .AddPolicy("CustomerOnly", policy => policy.RequireRole("customer"));

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
// MVC Controllers (for webhook endpoints)
// ============================================================================
builder.Services.AddControllers();

// ============================================================================
// JSON Source Generation for Native AOT
// ============================================================================
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, NetCommerce.Api.Serialization.ApiJsonContext.Default);
    options.SerializerOptions.Converters.Add(new NetCommerce.Kernel.Core.Serialization.StronglyTypedIdJsonConverterFactory());
});

// If using MVC controllers:
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.Converters.Add(new NetCommerce.Kernel.Core.Serialization.StronglyTypedIdJsonConverterFactory());
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
    await app.Services.ApplyMigrationsAsync<CatalogDbContext>();
    await app.Services.ApplyMigrationsAsync<OrderingDbContext>();
    await app.Services.ApplyMigrationsAsync<InventoryDbContext>();
    await app.Services.ApplyMigrationsAsync<PaymentsDbContext>();
    await app.Services.ApplyMigrationsAsync<FinanceDbContext>();
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
app.UseCors("AllowAll");
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
app.MapEndpointGroups(versionSet); // <--- Pass the versionSet here!
app.MapAllEndpoints(versionSet); // REPR Pattern: Vertical Slice Endpoints

// ============================================================================
// Map MVC Controllers
// ============================================================================
app.MapControllers();

app.Run();
