using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence;

namespace NetCommerce.Domain.Tests.Auditing;

public class AuditRepositoryTests : IDisposable
{
    private readonly DbContextOptions<TestDbContext> _options;

    public AuditRepositoryTests()
    {
        // 2025 Best Practice: Use SQLite in-memory for relational integrity testing
        _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Filename=:memory:")
            .Options;
    }

    private TestDbContext CreateDbContext()
    {
        var context = new TestDbContext(_options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task GetTimelineAsync_ShouldReturnChronologicalEntries()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new AuditRepository(context);
        var orderId = Guid.NewGuid().ToString();

        await repository.StoreAsync(AuditEntry.Create("u1", "C", "Order.Created", orderId, "Order", "{}", "c1"));
        await Task.Delay(10);
        await repository.StoreAsync(AuditEntry.Create("u1", "C", "Order.Paid", orderId, "Order", "{}", "c2"));

        // Act
        var timeline = await repository.GetTimelineAsync(orderId);

        // Assert
        timeline.Count.ShouldBe(2);
        timeline[0].Action.ShouldBe("Order.Created");
        timeline[1].Action.ShouldBe("Order.Paid");
    }

    [Fact]
    public async Task QueryAsync_WithFilters_ShouldReturnCorrectResults()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new AuditRepository(context);
        var baseTime = DateTime.UtcNow;

        // Create test entries with different properties
        await repository.StoreAsync(AuditEntry.Create("admin1", "Admin", "Price.Changed", "prod1", "Catalog", "{}", "corr1"));
        await repository.StoreAsync(AuditEntry.Create("user1", "Customer", "Order.Created", "order1", "Ordering", "{}", "corr2"));
        await repository.StoreAsync(AuditEntry.Create("admin1", "Admin", "Refund.Issued", "order2", "Payments", "{}", "corr3"));

        // Act - Query with multiple filters
        var results = await repository.QueryAsync(
            startDate: baseTime.AddMinutes(-1),
            endDate: baseTime.AddMinutes(1),
            userId: "admin1",
            module: "Catalog",
            action: "Price.Changed");

        // Assert
        results.Count.ShouldBe(1);
        results[0].UserId.ShouldBe("admin1");
        results[0].Module.ShouldBe("Catalog");
        results[0].Action.ShouldBe("Price.Changed");
    }

    [Fact]
    public async Task QueryAsync_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new AuditRepository(context);

        // Create multiple entries
        for (int i = 0; i < 5; i++)
        {
            await repository.StoreAsync(AuditEntry.Create($"user{i}", "Customer", $"Action.{i}", $"res{i}", "Test", "{}", $"corr{i}"));
        }

        // Act - Query with limit of 3
        var results = await repository.QueryAsync(limit: 3);

        // Assert
        results.Count.ShouldBe(3);
        // Results should be ordered by timestamp descending (most recent first)
        results[0].Timestamp.ShouldBeGreaterThan(results[1].Timestamp);
        results[1].Timestamp.ShouldBeGreaterThan(results[2].Timestamp);
    }

    public void Dispose()
    {
        using var context = new TestDbContext(_options);
        context.Database.EnsureDeleted();
    }

    // Internal DbContext for testing
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
        public DbSet<AuditEntry> AuditLogs => Set<AuditEntry>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<AuditEntry>().ToTable("audit_logs");
    }
}
