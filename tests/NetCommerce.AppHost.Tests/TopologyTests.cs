using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Projects;
using Xunit;

namespace NetCommerce.AppHost.Tests;

public class TopologyTests
{
    [Fact]
    public async Task AppHost_ShouldHaveCorrectResourcesAndDependencies()
    {
        // Arrange: Create the test builder
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<NetCommerce_AppHost>();

        // Keep test output lean to avoid VS Code freezing on large log volumes.
        appHost.Configuration["Logging:LogLevel:Default"] = "Warning";
        appHost.Configuration["Logging:LogLevel:Microsoft"] = "Warning";
        appHost.Configuration["Logging:LogLevel:Aspire"] = "Warning";

        // Disable DCP (Docker Compose Protocol) and related services to prevent background service failures in test environment
        appHost.Configuration["Dcp:Enabled"] = "false";
        appHost.Configuration["Dcp:ContainerRuntime"] = "none";
        appHost.Configuration["Dcp:Orchestrator:Enabled"] = "false";

        // Configure background service exception behavior to ignore failures instead of stopping the host
        appHost.Configuration["HostOptions:BackgroundServiceExceptionBehavior"] = "Ignore";

        // Mock the secret parameter "PostgresPassword" to prevent build errors
        appHost.Configuration["Parameters:PostgresPassword"] = "test-password";

        // Act: Build the application model (We do NOT start the containers here, just the model)
        await using var app = await appHost.BuildAsync();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        // =====================================================================
        // Assert 1: Verify Core Container Resources Exist
        // =====================================================================
        var postgres = model.Resources.Single(r => r.Name == "postgres");
        var redis = model.Resources.Single(r => r.Name == "redis");
        var keycloak = model.Resources.Single(r => r.Name == "keycloak");
        var seq = model.Resources.Single(r => r.Name == "seq");
        var api = model.Resources.Single(r => r.Name == "netcommerce-api");

        // Verify logical database resources exist (created by .AddDatabase)
        var catalogDb = model.Resources.OfType<PostgresDatabaseResource>().Single(r => r.Name == "CatalogDb");
        var orderingDb = model.Resources.OfType<PostgresDatabaseResource>().Single(r => r.Name == "OrderingDb");

        // =====================================================================
        // Assert 2: Verify API Dependencies (References)
        // =====================================================================
        var projectResource = (ProjectResource)api;
        var dependencies = projectResource.Annotations
            .OfType<ResourceRelationshipAnnotation>()
            .Select(a => a.Resource)
            .ToList();

        // The API must reference the *logical database*, not just the postgres container
        Assert.Contains(catalogDb, dependencies);
        Assert.Contains(orderingDb, dependencies);

        // The API must reference Redis, Seq, and Keycloak
        Assert.Contains(redis, dependencies);
        Assert.Contains(seq, dependencies);
        Assert.Contains(keycloak, dependencies);

        // =====================================================================
        // Assert 3: Verify Persistence (Volumes)
        // =====================================================================
        // Helper function to check for volume mounts
        void AssertHasDataVolume(IResource resource)
        {
            var mounts = resource.Annotations.OfType<ContainerMountAnnotation>();

            // CORRECTION: The specific enum for .WithDataVolume() is DockerVolume
            Assert.Contains(mounts, m => m.Type == ContainerMountType.Volume);
        }

        AssertHasDataVolume(postgres);
        AssertHasDataVolume(redis);
        AssertHasDataVolume(keycloak);
        AssertHasDataVolume(seq);

        // =====================================================================
        // Assert 4: Verify Service Discovery / Endpoints
        // =====================================================================
        // Ensure Keycloak has the realm imported
        var keycloakArgs = keycloak.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.NotEmpty(keycloakArgs);
        // Note: Specific args are evaluated at runtime, but we can check the resource structure exists.
    }

    [Fact]
    public async Task Api_Should_Have_Correct_Connection_Strings_Injected()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<NetCommerce_AppHost>();

        // Keep test output lean to avoid VS Code freezing on large log volumes.
        appHost.Configuration["Logging:LogLevel:Default"] = "Warning";
        appHost.Configuration["Logging:LogLevel:Microsoft"] = "Warning";
        appHost.Configuration["Logging:LogLevel:Aspire"] = "Warning";

        // Disable DCP (Docker Compose Protocol) and related services to prevent background service failures in test environment
        appHost.Configuration["Dcp:Enabled"] = "false";
        appHost.Configuration["Dcp:ContainerRuntime"] = "none";
        appHost.Configuration["Dcp:Orchestrator:Enabled"] = "false";

        // Configure background service exception behavior to ignore failures instead of stopping the host
        appHost.Configuration["HostOptions:BackgroundServiceExceptionBehavior"] = "Ignore";

        appHost.Configuration["Parameters:PostgresPassword"] = "test-password";

        // Act
        await using var app = await appHost.BuildAsync();

        // 1. Get the Resource from the built model
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var apiResource = model.Resources.Single(r => r.Name == "netcommerce-api");

        // 2. Setup the execution context for "Publish" mode (simulates deployment config)
        var options = new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)
        {
            ServiceProvider = app.Services
        };
        var context = new DistributedApplicationExecutionContext(options);

        // 3. Use ExecutionConfigurationBuilder to resolve binding expressions
        // This replaces the obsolete .GetEnvironmentVariableValuesAsync()
        var config = await ExecutionConfigurationBuilder
            .Create(apiResource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(context);

        var envVars = config.EnvironmentVariables;

        // Assert
        // Check for Standard Aspire Connection Strings
        Assert.Contains(envVars, kvp => kvp.Key == "ConnectionStrings__CatalogDb");
        Assert.Contains(envVars, kvp => kvp.Key == "ConnectionStrings__redis");

        // Check for Custom Environment Variables
        Assert.Contains(envVars, kvp => kvp.Key == "Auth__Audience" && kvp.Value == "netcommerce-api");
    }
}
