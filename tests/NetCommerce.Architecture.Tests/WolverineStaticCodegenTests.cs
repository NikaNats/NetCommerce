#nullable enable
using System.Reflection;
using NetCommerce.Api.Serialization;
using Shouldly;
using Wolverine;
using Wolverine.Attributes;
using Xunit;

namespace NetCommerce.Architecture.Tests;

/// <summary>
///     Verifies that every Wolverine message handler has a matching pre-generated static handler
///     under src/Api/Internal/Generated/. Under Native AOT, runtime compilation via Roslyn
///     (WolverineFx.RuntimeCompilation) is stripped from Release binaries, so a handler that is
///     not present in the static codegen output simply does not exist in production.
/// </summary>
public sealed class WolverineStaticCodegenTests
{
    private static readonly string[] CommandAssemblies =
    [
        "NetCommerce.Catalog.Application",
        "NetCommerce.Catalog.Infrastructure",
        "NetCommerce.Ordering.Application",
        "NetCommerce.Ordering.Infrastructure",
        "NetCommerce.Inventory.Application",
        "NetCommerce.Inventory.Infrastructure",
        "NetCommerce.Payments.Application",
        "NetCommerce.Payments.Infrastructure",
        "NetCommerce.Finance.Application",
        "NetCommerce.Finance.Infrastructure",
        "NetCommerce.Shipping.Application",
        "NetCommerce.Shipping.Infrastructure",
        "NetCommerce.Basket.Application",
        "NetCommerce.Media.Application"
    ];

    [Fact]
    public void GeneratedCodeFolder_MustExist_AndNotBeEmpty()
    {
        var basePath = ResolveApiGeneratedDirectory();
        Directory.Exists(basePath).ShouldBeTrue(
            $"Wolverine generated handler directory does not exist at '{basePath}'. Run 'dotnet run --project src/Api -- codegen write'.");

        var sourceFiles = Directory.GetFiles(basePath, "*.cs", SearchOption.AllDirectories);

        sourceFiles.Length.ShouldBeGreaterThan(0,
            "Wolverine static generated directory is empty. You must generate static code before shipping Native AOT builds.");
    }

    [Fact]
    public void EveryCommandHandler_MustHaveCorrespondingPreGeneratedWolverineHandlerFile()
    {
        var basePath = ResolveApiGeneratedDirectory();
        var generatedFiles = Directory.GetFiles(basePath, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        var missingHandlers = new List<string>();

        foreach (var assemblyName in CommandAssemblies)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                continue;
            }

            // Find all handler classes AND sagas decorated with [WolverineHandler], matching
            // Wolverine naming conventions, or registered as state-machine sagas via AddSagaType
            // (sagas end in "Saga" and carry no handler attribute, but their Handle() chains are
            // still statically code-generated and must not drift).
            var handlerClasses = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && (
                    t.GetCustomAttribute<WolverineHandlerAttribute>() != null ||
                    t.Name.EndsWith("Handler") ||
                    typeof(Wolverine.Saga).IsAssignableFrom(t)))
                .ToList();

            foreach (var handler in handlerClasses)
            {
                var handleMethods = handler.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                    // 'Start'/'StartAsync' covers saga-initiating messages (e.g. OrderFulfillmentSaga.Start)
                    .Where(m => m.Name is "Handle" or "HandleAsync" or "Consume" or "ConsumeAsync" or "Start" or "StartAsync" || m.GetCustomAttribute<WolverineHandlerAttribute>() != null);

                foreach (var method in handleMethods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0) continue;

                    var messageType = parameters[0].ParameterType;

                    // Ensure this message handler appears inside the compiled Wolverine code
                    var handlerName = handler.Name;
                    var messageTypeName = messageType.Name;

                    var isGenerated = generatedFiles.Any(content =>
                        content.Contains(handlerName) && content.Contains(messageTypeName));

                    if (!isGenerated)
                    {
                        missingHandlers.Add($"{handler.FullName}.{method.Name}({messageType.FullName})");
                    }
                }
            }
        }

        if (missingHandlers.Count > 0)
        {
            var message = "The following message handlers are not compiled into the static Wolverine codegen output:" +
                          Environment.NewLine + string.Join(Environment.NewLine, missingHandlers) +
                          Environment.NewLine + "Fix: Run 'dotnet run --project src/Api/NetCommerce.Api.csproj -- codegen write' and commit the output in src/Api/Internal/Generated/.";
            throw new Xunit.Sdk.XunitException(message);
        }
    }

    [Fact]
    public void RuntimeCompilation_MustNotBeReferencedInReleaseBuild()
    {
        var apiAssembly = typeof(ApiJsonContext).Assembly;

        // WolverineFx.RuntimeCompilation is intentionally referenced in Debug builds for local
        // developer iteration; the guarantee only applies to Release artifacts (what CI/tests build).
        var configuration = apiAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;
        if (!string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip(
                $"API assembly was built in '{configuration}' configuration. " +
                "The RuntimeCompilation exclusion is only enforced against Release builds.");
        }

        var referencedAssemblies = apiAssembly.GetReferencedAssemblies();

        var hasRoslynCompilation = referencedAssemblies.Any(a => a.Name?.Contains("WolverineFx.RuntimeCompilation") == true);

        hasRoslynCompilation.ShouldBeFalse(
            "Security/AOT Violation: WolverineFx.RuntimeCompilation is referenced by the production API assembly. " +
            "Ensure it is conditioned to Debug only in NetCommerce.Api.csproj.");
    }

    private static string ResolveApiGeneratedDirectory()
    {
        var probePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Api", "Internal", "Generated"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Api", "Internal", "Generated"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "Api", "Internal", "Generated"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Api/Internal/Generated"))
        };

        foreach (var path in probePaths)
        {
            if (Directory.Exists(path)) return Path.GetFullPath(path);
        }

        return Path.GetFullPath(probePaths[0]);
    }
}
