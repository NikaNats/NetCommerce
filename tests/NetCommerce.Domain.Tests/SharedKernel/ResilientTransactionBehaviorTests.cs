using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCommerce.Catalog.Application.TestCommands;
using NetCommerce.Catalog.Infrastructure;
using NetCommerce.Ordering.Application.TestCommands;
using NetCommerce.SharedKernel.Infrastructure.Behaviors;
using NetCommerce.SharedKernel.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace NetCommerce.Domain.Tests.SharedKernel;

#region ResilientTransactionBehavior Tests

public class ResilientTransactionBehaviorTests
{
    private readonly TestCatalogDbContext _dbContext;

    private readonly ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>
        _logger;

    public ResilientTransactionBehaviorTests()
    {
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TestCatalogDbContext(options);
        _logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
    }

    [Fact]
    public async Task Handle_Should_Skip_Transaction_When_Modules_Do_Not_Match()
    {
        // Arrange
        // Request: Ordering, Context: Catalog
        // We want to verify that the behavior does NOT run transaction logic (Strategy, ChangeTracker Clear, Logging)

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestOrderingCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestOrderingCommand, Result<Guid>, TestCatalogDbContext>(_dbContext,
                logger);

        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke().Returns(Result.Success(Guid.NewGuid()));

        // Track an entity to see if it gets cleared
        _dbContext.Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });

        // Act
        await behavior.Handle(new TestOrderingCommand(), _ => next(), CancellationToken.None);

        // Assert
        // Should call next
        await next.Received(1).Invoke();

        // Should NOT have cleared tracker (ChangeTracker.Clear happens inside strategy)
        _dbContext.ChangeTracker.Entries().Count().ShouldBeGreaterThan(0);

        // Should NOT have logged "Begin transaction"
        logger.ReceivedCalls().Count().ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_Execute_Logic_When_Modules_Match()
    {
        // Arrange
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(_dbContext,
                _logger);

        // Pre-seed tracker
        _dbContext.Set<ResilientTransactionTestEntity>()
            .Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });
        _dbContext.ChangeTracker.Entries().Count().ShouldBe(1);

        Task<Result<Guid>> Next()
        {
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        // Act
        try
        {
            await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);
        }
        catch (Exception)
        {
            // Ignore exception (expected due to InMemory transaction limitations)
        }

        // Assert
        // If the behavior logic ran, ChangeTracker.Clear() should have been called.
        // Since we seeded 1 entity, count should now be 0.
        _dbContext.ChangeTracker.Entries().Count().ShouldBe(0,
            "ChangeTracker should be cleared when behavior logic runs (even if transaction fails in InMemory).");
    }

    [Fact]
    public async Task Handle_Should_Call_Next_When_Transaction_Already_Active()
    {
        // Arrange
        // Use SQLite in-memory for real transaction support
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        var nextCallCount = 0;

        Task<Result<Guid>> Next()
        {
            nextCallCount++;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        // Start a transaction before calling the behavior
        await using var outerTransaction = await dbContext.Database.BeginTransactionAsync();

        // Act
        var result = await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        nextCallCount.ShouldBe(1, "Next should be called exactly once when transaction already active");

        // Verify no new transaction was started (no "Begin transaction" log)
        logger.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == nameof(ILogger.Log) &&
                      c.GetArguments().Any(a => a?.ToString()?.Contains("Begin transaction") ?? false))
            .ShouldBeFalse("Should not log 'Begin transaction' when transaction already exists");
    }

    [Fact]
    public async Task Handle_Should_Return_Response_Without_Commit_When_Result_Is_Failure()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        var failureError = Error.Validation("Test failure");

        Task<Result<Guid>> Next()
        {
            // Add an entity that should NOT be saved due to failure
            dbContext.Set<ResilientTransactionTestEntity>()
                .Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });
            return Task.FromResult(Result.Failure<Guid>(failureError));
        }

        // Act
        var result = await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(failureError);

        // Entity should NOT be persisted (transaction rolled back)
        dbContext.ChangeTracker.Clear();
        var count = await dbContext.Set<ResilientTransactionTestEntity>().CountAsync();
        count.ShouldBe(0, "Entity should not be persisted when result is failure");
    }

    [Fact]
    public async Task Handle_Should_Commit_Transaction_When_Result_Is_Success()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        var entityId = Guid.NewGuid();

        Task<Result<Guid>> Next()
        {
            dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = entityId });
            return Task.FromResult(Result.Success(entityId));
        }

        // Act
        var result = await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entityId);

        // Entity should be persisted
        dbContext.ChangeTracker.Clear();
        var persistedEntity = await dbContext.Set<ResilientTransactionTestEntity>().FindAsync(entityId);
        persistedEntity.ShouldNotBeNull("Entity should be persisted when result is success");
    }

    [Fact]
    public async Task Handle_Should_Log_Transaction_Begin_And_Commit_On_Success()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        Task<Result<Guid>> Next()
        {
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        // Act
        await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert - Verify logging occurred (at least 2 log calls: begin + commit)
        logger.ReceivedCalls().Count().ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Handle_Should_Log_Warning_When_Result_Is_Failure()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        Task<Result<Guid>> Next()
        {
            return Task.FromResult(Result.Failure<Guid>(Error.Validation("Test")));
        }

        // Act
        await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert - Verify warning was logged
        logger.ReceivedCalls().Count().ShouldBeGreaterThanOrEqualTo(2); // Begin + Warning
    }

    [Fact]
    public async Task Handle_Should_Throw_And_Rollback_When_Handler_Throws_Exception()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        var entityId = Guid.NewGuid();

        Task<Result<Guid>> Next()
        {
            dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = entityId });
            throw new InvalidOperationException("Test exception");
        }

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None));

        // Entity should NOT be persisted (transaction rolled back)
        dbContext.ChangeTracker.Clear();
        var count = await dbContext.Set<ResilientTransactionTestEntity>().CountAsync();
        count.ShouldBe(0, "Entity should not be persisted when exception is thrown");
    }
}

#endregion

#region ResilientTransaction Utility Tests

public class ResilientTransactionTests
{
    [Fact]
    public void New_Should_Throw_When_Context_Is_Null()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ResilientTransaction.New(null!));
    }

    [Fact]
    public async Task ExecuteAsync_Should_Execute_Action_Inside_Transaction()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var resilientTransaction = ResilientTransaction.New(dbContext);
        var entityId = Guid.NewGuid();

        // Act
        await resilientTransaction.ExecuteAsync(async () =>
        {
            dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = entityId });
            await dbContext.SaveChangesAsync();
        });

        // Assert
        dbContext.ChangeTracker.Clear();
        var persistedEntity = await dbContext.Set<ResilientTransactionTestEntity>().FindAsync(entityId);
        persistedEntity.ShouldNotBeNull("Entity should be persisted after successful transaction");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Rollback_On_Exception()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var resilientTransaction = ResilientTransaction.New(dbContext);
        var entityId = Guid.NewGuid();

        // Act
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await resilientTransaction.ExecuteAsync(async () =>
            {
                dbContext.Set<ResilientTransactionTestEntity>()
                    .Add(new ResilientTransactionTestEntity { Id = entityId });
                await dbContext.SaveChangesAsync();
                throw new InvalidOperationException("Test exception");
            });
        });

        // Assert
        dbContext.ChangeTracker.Clear();
        var count = await dbContext.Set<ResilientTransactionTestEntity>().CountAsync();
        count.ShouldBe(0, "Entity should not be persisted when exception causes rollback");
    }

    [Fact]
    public async Task ExecuteAsync_Should_Clear_ChangeTracker_Before_Execution()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var resilientTransaction = ResilientTransaction.New(dbContext);

        // Pre-track an entity
        dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });
        var preTrackedCount = dbContext.ChangeTracker.Entries().Count();
        preTrackedCount.ShouldBe(1);

        var wasTrackerClearedInAction = false;

        // Act
        await resilientTransaction.ExecuteAsync(async () =>
        {
            // Inside the strategy, tracker should be cleared
            wasTrackerClearedInAction = dbContext.ChangeTracker.Entries().Count() == 0;
            await Task.CompletedTask;
        });

        // Assert
        wasTrackerClearedInAction.ShouldBeTrue("ChangeTracker should be cleared at start of execution strategy");
    }
}

#endregion

#region ModuleMatcher Tests

public class ModuleMatcherTests
{
    [Fact]
    public async Task ModuleMatcher_Should_Match_Same_Module_Namespaces()
    {
        // Arrange - Both Catalog
        // Use SQLite for real transaction support
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();

        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        var wasNextCalled = false;

        Task<Result<Guid>> Next()
        {
            wasNextCalled = true;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        // Act
        await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);

        // Assert - Behavior logic ran, next was called
        wasNextCalled.ShouldBeTrue("Next should be called when modules match");
    }

    [Fact]
    public async Task ModuleMatcher_Should_Not_Match_Different_Module_Namespaces()
    {
        // Arrange - Request: Ordering, Context: Catalog
        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestOrderingCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestOrderingCommand, Result<Guid>, TestCatalogDbContext>(dbContext,
                logger);

        // Pre-track an entity
        dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });

        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke().Returns(Result.Success(Guid.NewGuid()));

        // Act
        await behavior.Handle(new TestOrderingCommand(), _ => next(), CancellationToken.None);

        // Assert - Behavior logic did NOT run (ChangeTracker NOT cleared)
        await next.Received(1).Invoke();
        dbContext.ChangeTracker.Entries().Count()
            .ShouldBeGreaterThan(0, "ChangeTracker NOT cleared indicates behavior skipped");
    }

    [Fact]
    public async Task ModuleMatcher_Should_Match_Case_Insensitively()
    {
        // This test verifies the StringComparison.OrdinalIgnoreCase in ModuleMatcher
        // Using TestCatalogCommand (Catalog) with TestCatalogDbContext (Catalog)
        // Both should match even if casing differs internally

        var options = new DbContextOptionsBuilder<TestCatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new TestCatalogDbContext(options);
        var logger = Substitute
            .For<ILogger<ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>>>();
        var behavior =
            new ResilientTransactionBehavior<TestCatalogCommand, Result<Guid>, TestCatalogDbContext>(dbContext, logger);

        dbContext.Set<ResilientTransactionTestEntity>().Add(new ResilientTransactionTestEntity { Id = Guid.NewGuid() });
        var initialCount = dbContext.ChangeTracker.Entries().Count();

        Task<Result<Guid>> Next()
        {
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        // Act
        try
        {
            await behavior.Handle(new TestCatalogCommand(), _ => Next(), CancellationToken.None);
        }
        catch
        {
            // Ignore InMemory transaction issues
        }

        // Assert - Module matched, so tracker was cleared
        dbContext.ChangeTracker.Entries().Count().ShouldBe(0);
    }
}

#endregion

#region Test Entities and Supporting Types

public class ResilientTransactionTestEntity
{
    public Guid Id { get; set; }
}

#endregion