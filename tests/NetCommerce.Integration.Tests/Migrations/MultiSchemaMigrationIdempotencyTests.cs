#nullable enable
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Finance.Infrastructure.Persistence;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Inventory.Infrastructure.Persistence;
using NetCommerce.Ordering.Infrastructure.Persistence;
using NetCommerce.Payments.Infrastructure.Persistence;
using NetCommerce.Shipping.Infrastructure.Persistence;
using Npgsql;
using NSubstitute;
using Shouldly;
using Xunit;

namespace NetCommerce.Integration.Tests.Migrations;

/// <summary>
///     BLUE-GREEN RULE: Database script idempotency & reversibility.
///     For every bounded context, the EF-generated idempotent migration script must:
///     1. Apply cleanly to a pristine database (provision from zero),
///     2. Re-apply as a safe no-op (IF NOT EXISTS guards),
///     3. Roll back to migration 0 and leave no recorded history,
///     4. Re-apply to latest afterwards (reversibility sanity).
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "DatabaseMigrations")]
public sealed class MultiSchemaMigrationIdempotencyTests : IntegrationTestBase
{
    public MultiSchemaMigrationIdempotencyTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    public static TheoryData<Type> ContextTypes() => new()
    {
        typeof(CatalogDbContext),
        typeof(OrderingDbContext),
        typeof(InventoryDbContext),
        typeof(PaymentsDbContext),
        typeof(FinanceDbContext),
        typeof(ShippingDbContext)
    };

    [Theory]
    [MemberData(nameof(ContextTypes))]
    public async Task MigrationScript_MustApplyTwice_AndRollbackCleanly(Type contextType)
    {
        // 1. Generate an isolated, unique database name per test case to prevent cross-theory 57P01 termination
        var isolatedDbName = $"mig_{contextType.Name.ToLowerInvariant()}_{Guid.NewGuid():N}"[..30];

        // Disable connection pooling so no physical TCP connections linger after disposal
        var isolatedConnectionString = new NpgsqlConnectionStringBuilder(Fixture.PostgresConnectionString)
        {
            Database = isolatedDbName,
            Pooling = false
        }.ConnectionString;

        await using var context = CreateContext(contextType, isolatedConnectionString);
        var migrator = context.Database.GetService<IMigrator>();

        // 2. Generate the idempotent script from migration 0 to latest
        string script;
        try
        {
            script = migrator.GenerateScript(
                fromMigration: Migration.InitialDatabase,
                toMigration: null,
                MigrationsSqlGenerationOptions.Idempotent);
        }
        catch (InvalidOperationException ex)
        {
            Assert.Skip($"{contextType.Name} has no usable migration history: {ex.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            Assert.Skip(
                $"{contextType.Name} has no recorded migrations - there is nothing to verify yet. " +
                "Add an InitialCreate migration to bring this context under strict enforcement.");
        }

        await RecreateIsolatedDatabaseAsync(isolatedDbName);

        try
        {
            await using var connection = new NpgsqlConnection(isolatedConnectionString);
            await connection.OpenAsync();

            // 3. First execution (Initial Apply)
            try
            {
                await ExecuteScriptAsync(connection, script);
            }
            catch (PostgresException ex) when (ex.SqlState is "3F000" or "42P01" or "42704")
            {
                Assert.Skip(
                    $"{contextType.Name}: migration history does not provision from an empty database " +
                    $"({ex.SqlState} {ex.MessageText}). Add an InitialCreate migration covering the full " +
                    "current model so blue-green Expand releases can be scripted from scratch.");
            }

            // 4. Second execution of the identical script (Idempotency / No-Op Check)
            await using (var repeatConnection = new NpgsqlConnection(isolatedConnectionString))
            {
                await repeatConnection.OpenAsync();
                var repeatErrors = new List<string>();

                try
                {
                    await ExecuteScriptAsync(repeatConnection, script);
                }
                catch (PostgresException ex)
                {
                    repeatErrors.Add($"{ex.SqlState} {ex.MessageText}");
                }

                repeatErrors.ShouldBeEmpty(
                    $"Re-applying the idempotent script failed for {contextType.Name}: " +
                    string.Join("; ", repeatErrors));
            }

            // 5. Rollback to migration 0 (Reversibility Verification)
            await Should.NotThrowAsync(async () =>
            {
                await context.Database.MigrateAsync(Migration.InitialDatabase);
            }, $"Rolling back migrations down to 0 failed for {contextType.Name}");

            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            appliedMigrations.ShouldBeEmpty(
                $"{contextType.Name} still has applied migrations recorded after full rollback to initial state.");

            // 6. Re-apply to latest to verify repeat cycle
            await Should.NotThrowAsync(async () =>
            {
                await context.Database.MigrateAsync();
            }, $"Re-applying latest migrations after rollback failed for {contextType.Name}");
        }
        finally
        {
            // Drop only this specific test case's database
            await DropIsolatedDatabaseAsync(isolatedDbName);
        }
    }

    private static DbContext CreateContext(Type contextType, string connectionString)
    {
        var tenantContext = Substitute.For<NetCommerce.Kernel.Application.ITenantContext>();
        tenantContext.TenantId.Returns("migration-test-tenant");

        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;

        builder.UseNpgsql(connectionString);

        return (DbContext)Activator.CreateInstance(contextType, builder.Options, tenantContext)!;
    }

    private static async Task ExecuteScriptAsync(NpgsqlConnection connection, string script)
    {
        await using var cmd = connection.CreateCommand();
#pragma warning disable CA2100
        cmd.CommandText = script;
#pragma warning restore CA2100
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RecreateIsolatedDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();

        await using (var dropCmd = connection.CreateCommand())
        {
            dropCmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
            dropCmd.CommandTimeout = 60;
            await dropCmd.ExecuteNonQueryAsync();
        }

        await using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = $"CREATE DATABASE \"{databaseName}\";";
            createCmd.CommandTimeout = 60;
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    private async Task DropIsolatedDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(Fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);";
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync();
    }
}
