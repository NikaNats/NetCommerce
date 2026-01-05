using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Hybrid;
using NetCommerce.Api.Authentication;
using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Inventory.Application.Stock.Commands;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Application.Transactions.Commands;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Infrastructure.Messaging;
using Wolverine.SignalR;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Add Aspire Service Defaults (OpenTelemetry, Health Checks, Service Discovery, Polly)
// ============================================================================
builder.AddServiceDefaults();

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
    typeof(RefundPaymentTransactionCommand)// Payments
);

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
// Authentication with Keycloak
// ============================================================================
// Bind Keycloak options from Aspire-injected environment variables (Keycloak__AuthServerUrl, Keycloak__Realm)
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Configure(options =>
    {
        // Override audience/scope from Auth__ section if provided
        var authSection = builder.Configuration.GetSection("Auth");
        if (!string.IsNullOrEmpty(authSection["Audience"]))
            options.Audience = authSection["Audience"]!;
        if (!string.IsNullOrEmpty(authSection["ApiScope"]))
            options.ApiScope = authSection["ApiScope"]!;
    })
    .ValidateOnStart();

builder.Services.ConfigureOptions<JwtBearerOptionsSetup>();

builder.Services.AddAuthentication()
    .AddJwtBearer();

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
// Module Registration
// ============================================================================
builder.Services.AddModules(builder.Configuration);

// ============================================================================
// OpenAPI / Swagger
// ============================================================================
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

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// SignalR Hub for Real-Time Order Notifications
// ============================================================================
// Wolverine's built-in WolverineHub provides WebSocket messaging to browsers.
// Frontend connects to this endpoint to receive order status updates.
app.MapWolverineSignalRHub("/api/messages");

// ============================================================================
// Map Minimal API Endpoints
// ============================================================================
app.MapEndpointGroups();

app.Run();
