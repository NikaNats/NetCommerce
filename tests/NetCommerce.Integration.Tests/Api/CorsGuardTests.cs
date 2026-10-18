#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetCommerce.Api.Extensions;
using Shouldly;

namespace NetCommerce.Integration.Tests.Api;

/// <summary>
///     Tests for the CORS fail-closed guard: <see cref="CorsOptionsValidator"/>
///     wired by <see cref="ServiceCollectionExtensions.AddApiServicesMinimal"/>
///     through ValidateOnStart.
/// </summary>
/// <remarks>
/// <para>
///     Each test boots a real host and calls StartAsync, so the guard sees the
///     REAL <see cref="IHostEnvironment"/> exactly as production startup does,
///     not an environment name sniffed from configuration.
/// </para>
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
    private static async Task StartHostAsync(
        string environment,
        string[]? origins = null,
        bool enforceCors = true)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment
        });

        var values = new Dictionary<string, string?>();
        if (origins is not null)
        {
            for (var i = 0; i < origins.Length; i++)
                values[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }
        builder.Configuration.AddInMemoryCollection(values);

        builder.Services.AddLogging();
        builder.Services.AddApiServicesMinimal(builder.Configuration, enforceCors);

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public async Task Serving_WithoutOrigins_OutsideDevAndTesting_ShouldFailStartup(string environment)
    {
        var ex = await Should.ThrowAsync<OptionsValidationException>(() => StartHostAsync(environment));

        ex.Message.ShouldContain("Cors:AllowedOrigins");
        ex.Message.ShouldContain(environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("UAT")]
    public async Task Serving_WithOnlyBlankOrigins_ShouldFailStartup(string environment)
    {
        await Should.ThrowAsync<OptionsValidationException>(() =>
            StartHostAsync(environment, ["  ", ""]));
    }

    [Fact]
    public async Task MigrateOnlyInProduction_WithoutOrigins_ShouldStart()
    {
        await Should.NotThrowAsync(() =>
            StartHostAsync("Production", enforceCors: false));
    }

    [Fact]
    public async Task ServingInProduction_WithOrigins_ShouldStart()
    {
        await Should.NotThrowAsync(() =>
            StartHostAsync("Production", ["https://shop.example.com"]));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData(CorsOptionsValidator.TestingEnvironment)]
    public async Task Serving_WithoutOrigins_InDevOrTesting_ShouldKeepLocalhostDefaults(string environment)
    {
        await Should.NotThrowAsync(() => StartHostAsync(environment));
    }
}
