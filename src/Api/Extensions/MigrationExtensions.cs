using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using Npgsql;
using Polly;

namespace NetCommerce.Api.Extensions;

public static class MigrationExtensions
{
    [RequiresDynamicCode("EF Core migrations are not supported with NativeAOT. Use migration bundles for production.")]
    public static async Task ApplyMigrationsAsync<TContext>(this IServiceProvider services)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        var retryPolicy = Policy
            .Handle<NpgsqlException>()
            .Or<System.Net.Sockets.SocketException>()
            .WaitAndRetryAsync(5,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (ex, time) => logger.LogWarning("DB not ready. Retrying in {Time}...", time));

        await retryPolicy.ExecuteAsync(async () =>
        {
            logger.LogInformation("Applying migrations for {Context}...", typeof(TContext).Name);
            await context.Database.MigrateAsync();
            logger.LogInformation("Migrations complete for {Context}.", typeof(TContext).Name);
        });
    }

    /// <summary>
    ///     Lists pending migrations per bounded-context database.
    ///     Used by the --migrate-only pipeline step to fail the STEP (not the
    ///     deploy) when the six independent applies leave anything behind.
    /// </summary>
    [RequiresDynamicCode("EF Core migration inspection requires dynamic code. JIT pipeline step only, never the AOT container.")]
    public static async Task<IReadOnlyList<(string Context, string[] Migrations)>> GetPendingMigrationsAsync(
        this IServiceProvider services)
    {
        var pending = new List<(string, string[])>();
        await Check<CatalogDbContext>();
        await Check<OrderingDbContext>();
        await Check<InventoryDbContext>();
        await Check<PaymentsDbContext>();
        await Check<FinanceDbContext>();
        await Check<ShippingDbContext>();
        return pending;

        async Task Check<TContext>() where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            var remaining = (await context.Database.GetPendingMigrationsAsync()).ToArray();
            if (remaining.Length > 0)
                pending.Add((typeof(TContext).Name, remaining));
        }
    }
}
