#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NetCommerce.Kernel.EfCore.Persistence;

namespace NetCommerce.Kernel.EfCore;

/// <summary>
/// Centralized Npgsql pooling configuration.
/// Prevents Postgres max_connections exhaustion (6 contexts × 100 default = 600 per pod).
/// Formula: Total App Connections = PodCount × Σ(MaxPoolSize per DbContext). For 3 pods × (6×20)=360 → set max_connections ≥400 or use PgBouncer.
/// </summary>
public static class NpgsqlPoolingExtensions
{
    /// <summary>
    /// Configures EF Core with strict Npgsql connection pooling to prevent Postgres exhaustion.
    /// </summary>
    public static IServiceCollection AddPooledKernelDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName,
        int maxPoolSize = 20) where TContext : BaseDbContext
    {
        var rawConnectionString = configuration.GetConnectionString(connectionStringName)
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException($"Missing connection string: {connectionStringName} or DefaultConnection");

        var builder = new NpgsqlConnectionStringBuilder(rawConnectionString)
        {
            MaxPoolSize = maxPoolSize,
            MinPoolSize = 2,
            ConnectionIdleLifetime = 300, // 5 minutes
            ConnectionPruningInterval = 60, // 1 minute
            Timeout = 15,
            // Keep other builder values from raw string (Host, Database, etc.)
        };

        // Preserve additional tuning if already present in raw string, but enforce strict limits above
        return services.AddKernelEfCore<TContext>(options =>
            options.UseNpgsql(builder.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", typeof(TContext).Name.Replace("DbContext", ""));
                // Need to resolve schema via reflection or use known schema; fallback to default
                // Use extension to set schema from TContext.Schema if available
                try
                {
                    var schemaProp = typeof(TContext).GetField("Schema", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var schema = schemaProp?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(schema))
                        npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", schema);
                }
                catch { /* fallback to default schema */ }

                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorCodesToAdd: null);
            }));
    }
}
