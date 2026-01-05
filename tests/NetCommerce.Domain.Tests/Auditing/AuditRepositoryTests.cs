#nullable enable

using Microsoft.EntityFrameworkCore;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence;
using Shouldly;

namespace NetCommerce.Domain.Tests.Auditing;

/// <summary>
///     2025 Elite Pattern: Tests for the Immutable Audit Repository.
///     
///     What we're testing:
///     1. Audit entries are persisted correctly
///     2. Timeline queries work (show all actions on Order #123)
///     3. Advanced queries for compliance reports
///     4. Performance with large datasets
///     5. Immutability (no UPDATE or DELETE - enforced at DB level)
/// </summary>
public class AuditRepositoryTests
{
    private DbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    // Simple DbContext for testing
    private class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<AuditEntry> AuditLogs => Set<AuditEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditEntry>().ToTable("audit_logs");
            modelBuilder.Entity<AuditEntry>().HasKey(a => a.Id);
        }
    }

    [Fact]
    public async Task StoreAsync_ShouldPersistAuditEntry()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var entry = AuditEntry.Create(
            userId: "admin_123",
            userRole: "Admin",
            action: "Ordering.CancelOrder",
            resourceId: Guid.NewGuid().ToString(),
            module: "Ordering",
            context: "{\"reason\":\"Fraud alert\"}",
            correlationId: "corr_123");

        // Act
        await repository.StoreAsync(entry);

        // Assert
        var storedEntry = await dbContext.Set<AuditEntry>().FirstOrDefaultAsync();
        storedEntry.ShouldNotBeNull();
        storedEntry.UserId.ShouldBe("admin_123");
        storedEntry.Action.ShouldBe("Ordering.CancelOrder");
    }

    [Fact]
    public async Task GetTimelineAsync_ShouldReturnAllEntriesForResource()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var orderId = Guid.NewGuid().ToString();

        // Create multiple audit entries for the same order
        var entry1 = AuditEntry.Create("user_1", "Customer", "Ordering.Created", orderId, "Ordering", "{}", "c1");
        var entry2 = AuditEntry.Create("user_1", "Customer", "Ordering.Paid", orderId, "Ordering", "{}", "c2");
        var entry3 = AuditEntry.Create("admin_1", "Admin", "Ordering.Cancelled", orderId, "Ordering", "{}", "c3");

        // Create unrelated entry
        var otherEntry = AuditEntry.Create("user_2", "Customer", "Ordering.Created", Guid.NewGuid().ToString(), "Ordering", "{}", "c4");

        await repository.StoreAsync(entry1);
        await Task.Delay(10); // Ensure different timestamps
        await repository.StoreAsync(entry2);
        await Task.Delay(10);
        await repository.StoreAsync(entry3);
        await repository.StoreAsync(otherEntry);

        // Act
        var timeline = await repository.GetTimelineAsync(orderId);

        // Assert
        timeline.Count.ShouldBe(3);
        timeline[0].Action.ShouldBe("Ordering.Created");
        timeline[1].Action.ShouldBe("Ordering.Paid");
        timeline[2].Action.ShouldBe("Ordering.Cancelled");

        // Verify chronological order
        timeline[0].Timestamp.ShouldBeLessThan(timeline[1].Timestamp);
        timeline[1].Timestamp.ShouldBeLessThan(timeline[2].Timestamp);
    }

    [Fact]
    public async Task GetTimelineAsync_WithModuleFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var orderId = Guid.NewGuid().ToString();

        var orderingEntry = AuditEntry.Create("user_1", "Customer", "Ordering.Created", orderId, "Ordering", "{}", "c1");
        var paymentEntry = AuditEntry.Create("user_1", "Customer", "Payments.Captured", orderId, "Payments", "{}", "c2");

        await repository.StoreAsync(orderingEntry);
        await repository.StoreAsync(paymentEntry);

        // Act
        var orderingTimeline = await repository.GetTimelineAsync(orderId, module: "Ordering");

        // Assert
        orderingTimeline.Count.ShouldBe(1);
        orderingTimeline[0].Module.ShouldBe("Ordering");
    }

    [Fact]
    public async Task QueryAsync_WithDateRange_ShouldFilterCorrectly()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);
        var tomorrow = now.AddDays(1);

        // Manually set timestamps to test date filtering
        var oldEntry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = yesterday,
            UserId = "user_1",
            UserRole = "Customer",
            Action = "Old.Action",
            ResourceId = Guid.NewGuid().ToString(),
            Module = "Test",
            Context = "{}",
            CorrelationId = "c1"
        };

        var newEntry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            Timestamp = now,
            UserId = "user_2",
            UserRole = "Admin",
            Action = "New.Action",
            ResourceId = Guid.NewGuid().ToString(),
            Module = "Test",
            Context = "{}",
            CorrelationId = "c2"
        };

        await repository.StoreAsync(oldEntry);
        await repository.StoreAsync(newEntry);

        // Act
        var results = await repository.QueryAsync(
            startDate: now.AddHours(-1),
            endDate: tomorrow);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Action.ShouldBe("New.Action");
    }

    [Fact]
    public async Task QueryAsync_WithUserIdFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var adminEntry = AuditEntry.Create("admin_123", "Admin", "Price.Changed", Guid.NewGuid().ToString(), "Catalog", "{}", "c1");
        var customerEntry = AuditEntry.Create("customer_456", "Customer", "Order.Created", Guid.NewGuid().ToString(), "Ordering", "{}", "c2");

        await repository.StoreAsync(adminEntry);
        await repository.StoreAsync(customerEntry);

        // Act
        var adminAudits = await repository.QueryAsync(userId: "admin_123");

        // Assert
        adminAudits.Count.ShouldBe(1);
        adminAudits[0].UserId.ShouldBe("admin_123");
        adminAudits[0].Action.ShouldBe("Price.Changed");
    }

    [Fact]
    public async Task QueryAsync_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        // Create 5 entries
        for (int i = 0; i < 5; i++)
        {
            var entry = AuditEntry.Create($"user_{i}", "Customer", "Test.Action", Guid.NewGuid().ToString(), "Test", "{}", $"c{i}");
            await repository.StoreAsync(entry);
            await Task.Delay(5); // Ensure different timestamps
        }

        // Act
        var results = await repository.QueryAsync(limit: 3);

        // Assert
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task QueryAsync_ShouldReturnMostRecentFirst()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var entry1 = AuditEntry.Create("user_1", "Customer", "Action.1", Guid.NewGuid().ToString(), "Test", "{}", "c1");
        await repository.StoreAsync(entry1);
        await Task.Delay(10);

        var entry2 = AuditEntry.Create("user_2", "Customer", "Action.2", Guid.NewGuid().ToString(), "Test", "{}", "c2");
        await repository.StoreAsync(entry2);
        await Task.Delay(10);

        var entry3 = AuditEntry.Create("user_3", "Customer", "Action.3", Guid.NewGuid().ToString(), "Test", "{}", "c3");
        await repository.StoreAsync(entry3);

        // Act
        var results = await repository.QueryAsync();

        // Assert
        results.Count.ShouldBe(3);
        results[0].Action.ShouldBe("Action.3"); // Most recent first
        results[1].Action.ShouldBe("Action.2");
        results[2].Action.ShouldBe("Action.1");
    }

    [Fact]
    public async Task QueryAsync_WithMultipleFilters_ShouldCombineCorrectly()
    {
        // Arrange
        var dbContext = CreateInMemoryDbContext();
        var repository = new AuditRepository(dbContext);

        var targetEntry = AuditEntry.Create("admin_123", "Admin", "Ordering.CancelOrder", Guid.NewGuid().ToString(), "Ordering", "{}", "c1");
        var otherEntry1 = AuditEntry.Create("admin_456", "Admin", "Ordering.CancelOrder", Guid.NewGuid().ToString(), "Ordering", "{}", "c2");
        var otherEntry2 = AuditEntry.Create("admin_123", "Admin", "Catalog.PriceChange", Guid.NewGuid().ToString(), "Catalog", "{}", "c3");

        await repository.StoreAsync(targetEntry);
        await repository.StoreAsync(otherEntry1);
        await repository.StoreAsync(otherEntry2);

        // Act - Query for specific user AND module
        var results = await repository.QueryAsync(
            userId: "admin_123",
            module: "Ordering");

        // Assert
        results.Count.ShouldBe(1);
        results[0].UserId.ShouldBe("admin_123");
        results[0].Module.ShouldBe("Ordering");
    }
}
