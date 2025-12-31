using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using NetCommerce.SharedKernel.Domain;
using NetCommerce.SharedKernel.Infrastructure.Persistence;
using NetCommerce.SharedKernel.Infrastructure.Persistence.Outbox;

namespace NetCommerce.Domain.Tests.SharedKernel;

/// <summary>
/// Unit tests for the Outbox pattern using InMemory database.
/// These tests verify the BaseDbContext outbox behavior without Docker.
/// </summary>
public class OutboxPatternTests : IDisposable
{
    private readonly TestDbContext _context;
    private readonly IMediator _mediator;

    public OutboxPatternTests()
    {
        _mediator = Substitute.For<IMediator>();
        
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new TestDbContext(options, _mediator);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    #region BaseDbContext Outbox Tests

    [Fact]
    public async Task SaveChangesAsync_WithDomainEvents_ShouldCreateOutboxMessages()
    {
        // Arrange
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("First Event");
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var outboxMessages = await _context.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
        
        var message = outboxMessages.First();
        message.Type.ShouldContain(nameof(TestDomainEvent));
        message.ProcessedOn.ShouldBeNull();
        message.RetryCount.ShouldBe(0);
    }

    [Fact]
    public async Task SaveChangesAsync_WithMultipleDomainEvents_ShouldCreateMultipleOutboxMessages()
    {
        // Arrange
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("Event 1");
        entity.RaiseTestEvent("Event 2");
        entity.RaiseTestEvent("Event 3");
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var outboxMessages = await _context.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldSerializeDomainEventCorrectly()
    {
        // Arrange
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("My Message");
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var message = await _context.OutboxMessages.FirstAsync();
        message.Content.ShouldContain("My Message");
        
        // Verify JSON structure
        var jsonDoc = JsonDocument.Parse(message.Content);
        jsonDoc.RootElement.TryGetProperty("message", out var messageProperty).ShouldBeTrue();
        messageProperty.GetString().ShouldBe("My Message");
    }

    [Fact]
    public async Task SaveChangesAsync_EntityAndOutboxMessage_ShouldBeSavedAtomically()
    {
        // Arrange
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("Test Event");
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert - Both entity and outbox message should be saved
        var savedEntity = await _context.TestEntities.FirstOrDefaultAsync(e => e.Id == entity.Id);
        var outboxMessage = await _context.OutboxMessages.FirstOrDefaultAsync();

        savedEntity.ShouldNotBeNull();
        outboxMessage.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldClearDomainEventsAfterSave()
    {
        // Arrange
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("Event");
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert - Domain events should be cleared
        entity.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_MultipleSaves_ShouldOnlyCreateOutboxForNewEvents()
    {
        // Arrange - First save
        var entity = new TestEntity("Test Value");
        entity.RaiseTestEvent("Event 1");
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync();

        // Act - Second save with new event
        entity.RaiseTestEvent("Event 2");
        await _context.SaveChangesAsync();

        // Assert
        var outboxMessages = await _context.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChangesAsync_NoDomainEvents_ShouldNotCreateOutboxMessages()
    {
        // Arrange
        var entity = new TestEntity("No Events");
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var outboxMessages = await _context.OutboxMessages.ToListAsync();
        outboxMessages.ShouldBeEmpty();
    }

    #endregion

    #region OutboxMessage Serialization Tests

    [Fact]
    public async Task OutboxMessage_ShouldContainAssemblyQualifiedType()
    {
        // Arrange
        var entity = new TestEntity("Test");
        entity.RaiseTestEvent("Message");
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var message = await _context.OutboxMessages.FirstAsync();
        message.Type.ShouldContain(typeof(TestDomainEvent).FullName!);
        message.Type.ShouldContain(typeof(TestDomainEvent).Assembly.GetName().Name!);
    }

    [Fact]
    public async Task OutboxMessage_ShouldPreserveOccurredOnFromDomainEvent()
    {
        // Arrange
        var entity = new TestEntity("Test");
        entity.RaiseTestEvent("Message");
        var domainEvent = entity.DomainEvents.First() as TestDomainEvent;
        var expectedOccurredOn = domainEvent!.OccurredOn;
        
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync();

        // Assert
        var message = await _context.OutboxMessages.FirstAsync();
        message.OccurredOn.ShouldBe(expectedOccurredOn);
    }

    #endregion
}

#region Test Infrastructure

/// <summary>
/// Test DbContext that extends BaseDbContext for testing the outbox pattern.
/// </summary>
internal class TestDbContext : BaseDbContext
{
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public TestDbContext(DbContextOptions<TestDbContext> options, IMediator mediator) 
        : base(options, mediator)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<TestEntity>(builder =>
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Value).IsRequired();
        });
    }
}

/// <summary>
/// Test entity for testing domain events and outbox pattern.
/// </summary>
internal class TestEntity : Entity<Guid>
{
    public string Value { get; private set; }

    public TestEntity(string value)
    {
        Id = Guid.NewGuid();
        Value = value;
    }

    private TestEntity() : this(string.Empty) { }

    public void RaiseTestEvent(string message)
    {
        AddDomainEvent(new TestDomainEvent(Id, message));
    }
}

/// <summary>
/// Test domain event for testing outbox serialization.
/// </summary>
internal sealed record TestDomainEvent(Guid EntityId, string Message) : DomainEvent;

#endregion
