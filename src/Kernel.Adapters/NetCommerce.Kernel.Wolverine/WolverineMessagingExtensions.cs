#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine;

/// <summary>
///     Wolverine messaging configuration extensions for IHostBuilder.
/// </summary>
public static class WolverineMessagingExtensions
{
    /// <summary>
    ///     Configures Wolverine messaging with PostgreSQL transactional outbox.
    ///     Discovers handlers from the specified assemblies.
    /// </summary>
    public static IHostBuilder UseWolverineMessaging(
        this IHostBuilder hostBuilder,
        IConfiguration configuration,
        params Type[] handlerAssemblyMarkerTypes)
    {
        var connectionString = configuration.GetConnectionString("postgres")
                               ?? configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Database connection string not found.");

        return hostBuilder.UseWolverine(opts =>
        {
            // Discover handlers from all module assemblies
            foreach (var markerType in handlerAssemblyMarkerTypes)
            {
                opts.Discovery.IncludeAssembly(markerType.Assembly);
            }

            // All other configuration should be done via ConfigureKernelDefaults in Program.cs
        });
    }
}
