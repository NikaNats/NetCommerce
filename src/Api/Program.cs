using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using NetCommerce.Api.Extensions;
using NetCommerce.Api.Middleware;
using NetCommerce.Api.Authentication;
using NetCommerce.Api.Endpoints;

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

// Seq for structured logging (Aspire will configure OTLP endpoint)

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

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

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
app.UseMiddleware<IdempotencyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseNetCommerceOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// Map Minimal API Endpoints
// ============================================================================
app.MapEndpointGroups();

app.Run();
