using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NetCommerce.Api.Extensions;

public static class OpenApiExtensions
{
    /// <summary>
    /// Adds OpenAPI configuration with Keycloak OAuth2 support using built-in ASP.NET Core OpenAPI.
    /// </summary>
    public static IHostApplicationBuilder AddNetCommerceOpenApi(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenApi("v1", options =>
        {
            // Document transformer to add API info and security schemes
            options.AddDocumentTransformer<OpenApiDocumentTransformer>();
        });

        return builder;
    }

    /// <summary>
    /// Maps the OpenAPI endpoint and configures Swagger UI with Keycloak OAuth2 settings.
    /// </summary>
    public static WebApplication UseNetCommerceOpenApi(this WebApplication app)
    {
        // Map the OpenAPI document endpoint
        app.MapOpenApi();

        if (app.Environment.IsDevelopment())
        {
            var swaggerClientId = app.Configuration["SWAGGERUI_CLIENTID"] ?? "netcommerce-swagger";

            app.UseSwaggerUI(options =>
            {
                // Point to the built-in OpenAPI endpoint
                options.SwaggerEndpoint("/openapi/v1.json", "NetCommerce API V1");
                
                // OAuth2 configuration for Swagger UI
                options.OAuthClientId(swaggerClientId);
                options.OAuthUsePkce();
                options.OAuthScopes("netcommerce.api", "openid", "profile", "email");
                
                // Keycloak authorization endpoints
                options.OAuthAdditionalQueryStringParams(new Dictionary<string, string>
                {
                    ["prompt"] = "consent"
                });
                
                options.EnablePersistAuthorization();
                options.EnableDeepLinking();
                options.DisplayRequestDuration();
            });
        }

        return app;
    }
}

/// <summary>
/// Document transformer that adds API info and OAuth2 security scheme for Keycloak.
/// </summary>
internal sealed class OpenApiDocumentTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) 
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        // Set document info
        document.Info = new OpenApiInfo
        {
            Title = "NetCommerce API",
            Version = "v1",
            Description = "E-Commerce Modular Monolith API with Aspire",
            Contact = new OpenApiContact
            {
                Name = "NetCommerce Team",
                Email = "support@netcommerce.com"
            }
        };

        // Check if JWT Bearer authentication is configured
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            // Add OAuth2 security scheme for Keycloak
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                ["oauth2"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description = "Keycloak OAuth2 Authorization Code Flow (PKCE)",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri("http://localhost:8080/realms/netcommerce/protocol/openid-connect/auth"),
                            TokenUrl = new Uri("http://localhost:8080/realms/netcommerce/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string>
                            {
                                ["netcommerce.api"] = "Access to NetCommerce API",
                                ["openid"] = "OpenID Connect scope",
                                ["profile"] = "User profile information",
                                ["email"] = "User email address"
                            }
                        }
                    }
                },
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT Authorization header using the Bearer scheme."
                }
            };

            // Apply security requirement to all operations
            if (document.Paths is not null)
            {
                foreach (var pathItem in document.Paths.Values)
                {
                    if (pathItem.Operations is null) continue;
                    
                    foreach (var operation in pathItem.Operations.Values)
                    {
                        operation.Security ??= [];
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("oauth2", document)] = ["netcommerce.api"]
                        });
                    }
                }
            }
        }
    }
}
