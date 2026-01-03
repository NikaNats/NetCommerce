#nullable enable
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace NetCommerce.Api.Authentication;

/// <summary>
///     Configures JWT Bearer options for Keycloak authentication.
/// </summary>
public class JwtBearerOptionsSetup(IOptions<AuthOptions> authOptions, IHostEnvironment environment)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name == JwtBearerDefaults.AuthenticationScheme || string.IsNullOrEmpty(name)) Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var auth = authOptions.Value;

        options.Authority = auth.Authority;
        options.Audience = auth.Audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = auth.Authority,
            ValidAudience = auth.Audience,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                    context.Response.Headers.Append("Token-Expired", "true");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // Extract roles from Keycloak's realm_access and resource_access claims
                var claimsIdentity = context.Principal?.Identity as ClaimsIdentity;
                if (claimsIdentity == null) return Task.CompletedTask;

                // Extract realm roles
                var realmAccessClaim = context.Principal?.FindFirst("realm_access");
                if (realmAccessClaim != null)
                    try
                    {
                        var realmAccess = JsonDocument.Parse(realmAccessClaim.Value);
                        if (realmAccess.RootElement.TryGetProperty("roles", out var roles))
                            foreach (var role in roles.EnumerateArray())
                                claimsIdentity.AddClaim(new Claim("roles", role.GetString() ?? ""));
                    }
                    catch
                    {
                        /* Ignore parsing errors */
                    }

                // Extract resource roles for this API
                var resourceAccessClaim = context.Principal?.FindFirst("resource_access");
                if (resourceAccessClaim != null)
                    try
                    {
                        var resourceAccess = JsonDocument.Parse(resourceAccessClaim.Value);
                        if (resourceAccess.RootElement.TryGetProperty("netcommerce-api", out var apiAccess) &&
                            apiAccess.TryGetProperty("roles", out var apiRoles))
                            foreach (var role in apiRoles.EnumerateArray())
                                claimsIdentity.AddClaim(new Claim("permissions", role.GetString() ?? ""));
                    }
                    catch
                    {
                        /* Ignore parsing errors */
                    }

                return Task.CompletedTask;
            }
        };
    }
}
