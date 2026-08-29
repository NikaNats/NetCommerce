#nullable enable
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NetCommerce.Kernel.Application;
using NetCommerce.Kernel.Security.Authorization;

namespace NetCommerce.Kernel.Security.Authentication;

/// <summary>
///     Zero-Trust Identity Mesh Extensions for NetCommerce.
///     This extension standardizes how any service in your system:
///     1. Validates JWT tokens from the identity provider (Keycloak, Auth0, etc.)
///     2. Transforms nested role claims into flat .NET claims
///     3. Performs optional token introspection for instant revocation (kill switch)
///     4. Enables secure token exchange for downstream service calls
///     Usage in Program.cs:
///     <code>
///     builder.AddZeroTrustAuthentication();
///     // ...
///     app.UseZeroTrustMiddleware();
///     </code>
///     Identity Mesh Ready: This pattern is portable to microservices.
/// </summary>
public static class ZeroTrustAuthenticationExtensions
{
    /// <summary>
    ///     Adds Zero-Trust authentication infrastructure to the application.
    ///     Configures JWT Bearer authentication with identity provider, role transformation,
    ///     and optional token introspection.
    /// </summary>
    public static IHostApplicationBuilder AddZeroTrustAuthentication(
        this IHostApplicationBuilder builder,
        Action<ZeroTrustAuthOptions>? configureOptions = null)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // 1. Bind and configure options
        var optionsBuilder = services.AddOptions<ZeroTrustAuthOptions>()
            .Bind(configuration.GetSection(ZeroTrustAuthOptions.SectionName));

        // Also bind Keycloak section for Aspire-injected values
        services.AddOptions<ZeroTrustAuthOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                // Map Keycloak__AuthServerUrl to Authority
                var authServerUrl = config["Keycloak:AuthServerUrl"];
                if (!string.IsNullOrEmpty(authServerUrl))
                    options.Authority = authServerUrl;

                // Map Keycloak__Realm to Realm
                var realm = config["Keycloak:Realm"];
                if (!string.IsNullOrEmpty(realm))
                    options.Realm = realm;

                // Allow direct Auth section overrides
                var authSection = config.GetSection("Auth");
                if (!string.IsNullOrEmpty(authSection["Audience"]))
                    options.Audience = authSection["Audience"]!;
                if (!string.IsNullOrEmpty(authSection["ApiScope"]))
                    options.ApiScope = authSection["ApiScope"]!;
                if (!string.IsNullOrEmpty(authSection["ClientId"]))
                    options.ClientId = authSection["ClientId"]!;
                if (!string.IsNullOrEmpty(authSection["ClientSecret"]))
                    options.ClientSecret = authSection["ClientSecret"]!;
                if (bool.TryParse(authSection["IntrospectionEnabled"], out var introspectionEnabled))
                    options.IntrospectionEnabled = introspectionEnabled;
                if (int.TryParse(authSection["IntrospectionCacheSeconds"], out var cacheSeconds))
                    options.IntrospectionCacheSeconds = cacheSeconds;
                if (bool.TryParse(authSection["TokenExchangeEnabled"], out var tokenExchangeEnabled))
                    options.TokenExchangeEnabled = tokenExchangeEnabled;
            });

        if (configureOptions is not null)
            optionsBuilder.Configure(configureOptions);

        optionsBuilder.ValidateOnStart();

        // 2. Configure JWT Bearer Authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Options will be configured by JwtBearerOptionsSetup
            });

        // 3. Register JWT Bearer options configurator
        services.ConfigureOptions<ZeroTrustJwtBearerOptionsSetup>();

        // 4. Add Claims Transformation (Role Flattening)
        services.AddTransient<IClaimsTransformation, OidcRoleClaimsTransformation>();

        // 5. Register HTTP clients for introspection and token exchange
        services.AddHttpClient("KeycloakIntrospection")
            .ConfigureHttpClient((sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            });

        services.AddHttpClient("KeycloakTokenExchange")
            .ConfigureHttpClient((sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            });

        // 6. Register token exchange factory for downstream services
        services.AddSingleton<TokenExchangeHandlerFactory>();

        // 7. Add HttpContextAccessor for token retrieval
        services.AddHttpContextAccessor();

        // 8. Register HttpUserContext as IUserContext (Kernel.Application interface)
        services.AddScoped<IUserContext, HttpUserContext>();

        // 9. Register HttpTenantContext as ITenantContext (required by BaseDbContext multi-tenancy filters)
        services.AddScoped<ITenantContext, HttpTenantContext>();

        // 10. Register Keycloak BFF Token Proxy (typed HttpClient)
        // All token lifecycle management is delegated to Keycloak via this proxy.
        services.AddHttpClient<KeycloakTokenProxy>()
            .ConfigureHttpClient((_, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            });

        // 11. Register Admin Elevated Auth options (fail-closed, explicit posture)
        services.AddOptions<AdminElevatedAuthOptions>()
            .Bind(configuration.GetSection(AdminElevatedAuthOptions.SectionName))
            .ValidateOnStart();

        // Backward compat: keep old section bound as well for migration
        services.AddOptions<AdminApiKeyOptions>()
            .Bind(configuration.GetSection(AdminApiKeyOptions.SectionName));

        services.AddSingleton<IValidateOptions<AdminElevatedAuthOptions>, AdminElevatedAuthOptionsValidator>();

        // 12. Register Authorization Handlers
        services.AddSingleton<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, AdminElevatedAuthorizationHandler>();

        return builder;
    }

    /// <summary>
    ///     Adds the Zero-Trust middleware to the request pipeline.
    ///     This includes token introspection (kill switch) for instant revocation.
    ///     IMPORTANT: Call this AFTER UseAuthentication() and UseAuthorization().
    /// </summary>
    public static IApplicationBuilder UseZeroTrustMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TokenIntrospectionMiddleware>();
    }

    /// <summary>
    ///     Configures an HttpClient to use token exchange for downstream service calls.
    ///     Usage:
    ///     <code>
    ///     builder.Services.AddHttpClient("InventoryService")
    ///         .AddTokenExchange("inventory-service");
    ///     </code>
    /// </summary>
    public static IHttpClientBuilder AddTokenExchange(
        this IHttpClientBuilder builder,
        string targetAudience)
    {
        return builder.AddHttpMessageHandler(sp =>
        {
            var factory = sp.GetRequiredService<TokenExchangeHandlerFactory>();
            return factory.CreateHandler(targetAudience);
        });
    }
}

/// <summary>
///     Configures JWT Bearer options for Zero-Trust authentication with identity provider.
/// </summary>
internal sealed class ZeroTrustJwtBearerOptionsSetup(
    IOptions<ZeroTrustAuthOptions> authOptions,
    IHostEnvironment environment)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name == JwtBearerDefaults.AuthenticationScheme || string.IsNullOrEmpty(name))
            Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var auth = authOptions.Value;

        // Authority is the realm URL
        options.Authority = auth.RealmUrl;
        options.Audience = auth.Audience;

        // Don't remap claims - keep identity provider's original claim names
        options.MapInboundClaims = false;

        // Docker/K8s internal networking often uses HTTP
        // In production, your Gateway handles TLS termination
        options.RequireHttpsMetadata = !environment.IsDevelopment();

        // Token Validation Rules (Zero-Trust: Validate Everything)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.RealmUrl,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateLifetime = true,
            // Tight clock skew for security (30 seconds max)
            ClockSkew = TimeSpan.FromSeconds(30),
            // Use identity provider's preferred claim names
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };

        // CRITICAL: Save token for downstream exchange and introspection
        options.SaveToken = true;

        // Event handlers for logging and custom behavior
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                // Add header indicating token expiration
                if (context.Exception is SecurityTokenExpiredException)
                    context.Response.Headers["Token-Expired"] = "true";
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // Additional validation can be added here
                // Role transformation is handled by IClaimsTransformation
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // Customize 401 response if needed
                return Task.CompletedTask;
            }
        };
    }
}
