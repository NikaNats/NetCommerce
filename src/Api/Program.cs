using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using NetCommerce.Api.Authentication;
using NetCommerce.Api.Endpoints;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// Add Aspire Service Defaults (OpenTelemetry, Health Checks, Service Discovery, Polly)
// ============================================================================
builder.AddServiceDefaults();

// ============================================================================
// Problem Details for consistent error responses
// ============================================================================
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ============================================================================
// Aspire-managed services - automatically configured via AppHost references
// ============================================================================

// Redis (Aspire will inject the connection string)
builder.AddRedisClient("redis");

// Azure Blob Storage (Aspire will inject the connection string)
builder.AddAzureBlobServiceClient("blobs");

// Seq for structured logging (Aspire will configure OTLP endpoint)
builder.AddSeqEndpoint("seq");

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
// Automatic Database Initialization (Development only)
// ============================================================================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    // Catalog
    var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await catalogDb.Database.EnsureCreatedAsync();

    // Ordering
    var orderingDb = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await orderingDb.Database.EnsureCreatedAsync();

    // Inventory
    var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await inventoryDb.Database.EnsureCreatedAsync();

    // Payments
    var paymentsDb = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await paymentsDb.Database.EnsureCreatedAsync();
}

// ============================================================================
// Map Minimal API Endpoints
// ============================================================================
app.MapEndpointGroups();

app.Run();