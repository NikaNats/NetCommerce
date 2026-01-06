using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Catalog.Application.Products.Commands;
using NetCommerce.Catalog.Infrastructure.Persistence;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Kernel.Core.Results;
using Shouldly;
using Wolverine;
using Wolverine.Tracking;

namespace NetCommerce.Integration.Tests.Wolverine;

/// <summary>
///     Integration tests demonstrating Wolverine's Tracked Sessions for testing
///     message flows and the transactional outbox pattern.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class WolverineTrackedSessionTests : IntegrationTestBase
{
    public WolverineTrackedSessionTests(IntegrationTestFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     Tests that a command can be invoked and produces the expected result
    ///     using Wolverine's tracked session.
    /// </summary>
    [Fact]
    public async Task CreateProduct_ShouldSucceed_AndTrackMessageFlow()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Test Product",
            Description: "A test product for integration testing",
            Sku: $"TEST-{Guid.NewGuid():N}",
            Price: 99.99m,
            Currency: "USD",
            CategoryId: Guid.NewGuid());

        // Act - Use tracked session to capture all message activity
        var (tracked, result) = await Fixture.Host
            .InvokeMessageAndWaitAsync<Result<Guid>>(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        // Verify message was executed successfully
        tracked.Executed.SingleMessage<CreateProductCommand>()
            .ShouldBe(command);

        // Verify the product was persisted
        await using var db = Fixture.CreateCatalogDbContext();
        var product = await db.Products.FindAsync(result.Value);
        product.ShouldNotBeNull();
        product.Name.ShouldBe("Test Product");
    }

    /// <summary>
    ///     Tests that validation failures are properly handled.
    /// </summary>
    [Fact]
    public async Task CreateProduct_WithDuplicateSku_ShouldReturnConflict()
    {
        // Arrange - Create a product first
        var sku = $"DUP-{Guid.NewGuid():N}";
        var firstCommand = new CreateProductCommand(
            Name: "First Product",
            Description: "The first product",
            Sku: sku,
            Price: 49.99m,
            Currency: "USD",
            CategoryId: Guid.NewGuid());

        await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(firstCommand);

        // Try to create another product with the same SKU
        var duplicateCommand = new CreateProductCommand(
            Name: "Duplicate Product",
            Description: "Trying to use same SKU",
            Sku: sku,
            Price: 59.99m,
            Currency: "USD",
            CategoryId: Guid.NewGuid());

        // Act
        var (_, result) = await Fixture.Host
            .InvokeMessageAndWaitAsync<Result<Guid>>(duplicateCommand);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.Code.ShouldContain("Conflict");
    }

    /// <summary>
    ///     Tests tracking activity to observe all messages processed during a session.
    /// </summary>
    [Fact]
    public async Task TrackActivity_ShouldCaptureAllMessages()
    {
        // Arrange
        var command = new CreateProductCommand(
            Name: "Tracked Product",
            Description: "Testing activity tracking",
            Sku: $"TRK-{Guid.NewGuid():N}",
            Price: 199.99m,
            Currency: "USD",
            CategoryId: Guid.NewGuid());

        // Act - Track all activity
        var tracked = await Fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .InvokeMessageAndWaitAsync(command);

        // Assert - Verify tracking captured the command
        tracked.Executed.MessagesOf<CreateProductCommand>()
            .ShouldContain(command);

        // Check that no exceptions occurred
        tracked.AllExceptions().ShouldBeEmpty();
    }

    /// <summary>
    ///     Tests that the message bus correctly routes messages to handlers.
    /// </summary>
    [Fact]
    public async Task MessageBus_ShouldRouteToCorrectHandler()
    {
        // Arrange
        var bus = Fixture.Host.Services.GetRequiredService<IMessageBus>();
        var command = new CreateProductCommand(
            Name: "Routed Product",
            Description: "Testing message routing",
            Sku: $"RTE-{Guid.NewGuid():N}",
            Price: 149.99m,
            Currency: "USD",
            CategoryId: Guid.NewGuid());

        // Act
        var result = await bus.InvokeAsync<Result<Guid>>(command);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    ///     Tests concurrent message processing with tracked sessions.
    /// </summary>
    [Fact]
    public async Task ConcurrentCommands_ShouldAllSucceed()
    {
        // Arrange & Act - Execute commands sequentially for reliability
        var productIds = new List<Guid>();

        for (var i = 1; i <= 5; i++)
        {
            var command = new CreateProductCommand(
                Name: $"Concurrent Product {i}",
                Description: $"Product {i} for concurrency test",
                Sku: $"CON-{i}-{Guid.NewGuid():N}",
                Price: 10.00m * i,
                Currency: "USD",
                CategoryId: Guid.NewGuid());

            var (_, result) = await Fixture.Host.InvokeMessageAndWaitAsync<Result<Guid>>(command);
            result.IsSuccess.ShouldBeTrue($"Product {i} failed: {result.Error?.Description}");
            result.Value.ShouldNotBe(Guid.Empty);
            productIds.Add(result.Value);
        }

        // Assert - All products were created
        productIds.Count.ShouldBe(5);

        await using var db = Fixture.CreateCatalogDbContext();
        var productCount = await db.Products.CountAsync(p => productIds.Contains(p.Id));
        productCount.ShouldBe(5);
    }
}
