#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Api.Extensions;
using Shouldly;

namespace NetCommerce.Integration.Tests.Api;

/// <summary>
///     Unit tests for the CORS fail-closed guard in
///     <see cref="ServiceCollectionExtensions.AddApiServicesMinimal"/>.
/// </summary>
/// <remarks>
/// <para>
///     The native-AOT verification pipeline runs the API with
///     <c>--migrate-only</c> in Production to apply the six module schemas.
///     That runner never binds HTTP, so demanding storefront origins there
///     failed schema setup (exit 134) for a concern migration never exercises.
///     These tests pin both sides: serving stays fail-closed, migration skips
///     the serving-time check.
/// </para>
/// </remarks>
public sealed class CorsGuardTests
{
    private static IConfiguration Config(string? environment, string[]? origins = null)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null)
            values["ASPNETCORE_ENVIRONMENT"] = environment;
        if (origins is not null)
        {
            for (var i = 0; i < origins.Length; i++)
                values[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void ServingInProduction_WithoutOrigins_ShouldThrow()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<InvalidOperationException>(() =>
            services.AddApiServicesMinimal(Config("Production")));

        ex.Message.ShouldContain("Cors:AllowedOrigins");
    }

    [Fact]
    public void MigrateOnlyInProduction_WithoutOrigins_ShouldNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() =>
            services.AddApiServicesMinimal(Config("Production"), enforceCors: false));
    }

    [Fact]
    public void ServingInProduction_WithOrigins_ShouldNotThrow()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() =>
            services.AddApiServicesMinimal(
                Config("Production", ["https://shop.example.com"])));
    }

    [Fact]
    public void ServingInDevelopment_WithoutOrigins_ShouldKeepLocalhostDefaults()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() =>
            services.AddApiServicesMinimal(Config("Development")));
    }
}
