namespace NetCommerce.Kernel.Wolverine;

using global::Wolverine;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Reflection;
using Wolverine;

public static class WolverineMessagingExtensions
{
    public static bool IsRunningCodegen()
    {
        return Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"
            || Environment.GetCommandLineArgs().Contains("codegen");
    }

    public static IHostBuilder UseWolverineMessaging(
        this IHostBuilder hostBuilder,
        Action<WolverineOptions>? configure = null)
    {
        return hostBuilder.UseWolverine(opts =>
        {
            ApplyDefaults(opts);
            configure?.Invoke(opts);
        });
    }

    public static IHostBuilder UseWolverineMessaging(
        this IHostBuilder hostBuilder,
        IConfiguration configuration,
        Action<WolverineOptions>? configure = null,
        params Type[] assemblyMarkers)
    {
        return hostBuilder.UseWolverine(opts =>
        {
            ApplyDefaults(opts);

            if (assemblyMarkers is { Length: > 0 })
            {
                foreach (var marker in assemblyMarkers)
                {
                    opts.Discovery.IncludeAssembly(marker.Assembly);
                }
            }

            configure?.Invoke(opts);
        });
    }

    private static void ApplyDefaults(WolverineOptions opts)
    {
        // 1. Allow Service Location for EF Core DbContextOptions & factory services
        opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

        // 2. Set TypeLoadMode dynamically based on active environment
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
               ?? "Production";

        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(env, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        }
        else
        {
            opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
        }
    }
}
