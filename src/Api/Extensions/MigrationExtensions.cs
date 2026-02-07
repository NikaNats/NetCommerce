using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
}
