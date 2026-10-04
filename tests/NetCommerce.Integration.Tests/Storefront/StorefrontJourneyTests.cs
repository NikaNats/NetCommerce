#nullable enable
using Microsoft.Extensions.DependencyInjection;
using NetCommerce.Api.Endpoints.Basket;
using NetCommerce.Basket.Application;
using NetCommerce.Basket.Infrastructure;
using NetCommerce.Catalog.Domain.Categories;
using NetCommerce.Catalog.Domain.Products;
using NetCommerce.Catalog.Infrastructure.Persistence.Repositories;
using NetCommerce.Domain.Shared;
using NetCommerce.Integration.Tests.Fixtures;
using NetCommerce.Kernel.Core.Domain;
using NetCommerce.Kernel.Core.Results;
using NetCommerce.Ordering.Application.Orders.Commands;
using NetCommerce.Ordering.Application.Orders.Queries;
using NetCommerce.Ordering.Domain.Orders;
using Shouldly;
using Wolverine;

namespace NetCommerce.Integration.Tests.Storefront;

/// <summary>
///     Storefront user journeys, driven exactly as the Next.js frontend behaves:
///     browse → basket → checkout (double-click safe) → order-status reconcile →
///     cancel. Runs against real Postgres + Redis + Wolverine through the same
///     seams the HTTP endpoints use (repository → pricer → bus → queries),
///     minus the transport layer which has its own contract tests.
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class StorefrontJourneyTests(IntegrationTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task BrowseToCancel_AsTheStorefrontBehaves_ShouldWorkEndToEnd()
    {
        var customerId = Guid.NewGuid();
        var basketKey = $"journey-{customerId:N}";
        var idempotencyKey = $"journey-{Guid.NewGuid():N}";

        // ── BROWSE: published catalog with a FOOD product ────────────────────
        await using var catalogDb = Fixture.CreateCatalogDbContext();
        var category = Category.Create("Food", "Edible goods");
        catalogDb.Categories.Add(category);
        var apple = Product.Create("Apple", "Fresh apple", "SKU-JRN-APPLE", Money.Create(10m, "GEL"), category.Id);
        apple.Publish();
        ((IHasDomainEvents)apple).ClearDomainEvents();
        catalogDb.Products.Add(apple);
        var draft = Product.Create("Ghost", "Unpublished", "SKU-JRN-GHOST", Money.Create(1m, "GEL"), category.Id);
        ((IHasDomainEvents)draft).ClearDomainEvents();
        catalogDb.Products.Add(draft);
        await catalogDb.SaveChangesAsync();

        // ── BASKET: price comes from the catalog, never the request ─────────
        await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Fixture.RedisConnectionString);
        IBasketRepository baskets = new RedisBasketRepository(redis);
        var priceSource = new CatalogPriceSource(new ProductRepository(catalogDb));

        var basket = await baskets.GetBasketAsync(basketKey) ?? ShoppingBasket.Create(basketKey);
        var line = await BasketPricer.CreateLineAsync(priceSource, apple.Id, 2);
        line.Price.ShouldBe(10m);
        basket.AddItem(line);
        await baskets.UpdateBasketAsync(basket);

        var stored = await baskets.GetBasketAsync(basketKey);
        stored.ShouldNotBeNull();
        stored!.TotalPrice.ShouldBe(20m);

        // Transport contract: the endpoint returns a DTO, never the domain model.
        var dto = BasketMapper.ToDto(stored);
        dto.Items.Count.ShouldBe(1);
        dto.TotalPrice.ShouldBe(20m);

        // ── CHECKOUT: create order; user double-click is idempotent ─────────
        using var scope = Fixture.Host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var checkout = new CreateOrderCommand(
            customerId,
            "user@example.com",
            "Journey User",
            [new OrderItemRequest(apple.Id, 2)],
            new AddressDto("5 Rustaveli Ave", "Tbilisi", "TB", "0105", "GE", "Journey User", "+995500000000"),
            new AddressDto("5 Rustaveli Ave", "Tbilisi", "TB", "0105", "GE", "Journey User", "+995500000000"),
            "card",
            idempotencyKey);

        var created = await bus.InvokeAsync<Result<Guid>>(checkout);
        created.IsSuccess.ShouldBeTrue();
        var retried = await bus.InvokeAsync<Result<Guid>>(checkout with { });
        retried.IsSuccess.ShouldBeTrue();
        retried.Value.ShouldBe(created.Value);

        // FOOD is taxed at half the 18% base rate: 20.00 + round(20 * 0.09) = 21.80.
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var persisted = await orders.GetByIdAsync(created.Value);
        persisted.ShouldNotBeNull();
        persisted!.TotalAmount.Amount.ShouldBe(21.80m);
        persisted.TotalAmount.Currency.ShouldBe("GEL");

        // ── RECONCILE: storefront re-reads authoritative state ──────────────
        var read = await bus.InvokeAsync<Result<OrderDetailsDto>>(
            new GetOrderByIdQuery(created.Value, customerId));
        read.IsSuccess.ShouldBeTrue();
        read.Value.Status.ShouldBe(OrderStatus.Submitted);
        read.Value.OrderNumber.ShouldBe(persisted.OrderNumber);

        var stranger = await bus.InvokeAsync<Result<OrderDetailsDto>>(
            new GetOrderByIdQuery(created.Value, Guid.NewGuid()));
        stranger.IsSuccess.ShouldBeFalse();
        stranger.Error.Code.ShouldContain("Forbidden");

        // ── CANCEL: owner lookup then cancel, then no double-cancel ─────────
        var owner = await bus.InvokeAsync<Result<Guid>>(new GetOrderOwnerQuery(created.Value));
        owner.IsSuccess.ShouldBeTrue();
        owner.Value.ShouldBe(customerId);

        var cancelled = await bus.InvokeAsync<Result>(
            new CancelOrderCommand(created.Value, "Changed my mind."));
        cancelled.IsSuccess.ShouldBeTrue();

        var afterCancel = await bus.InvokeAsync<Result<OrderDetailsDto>>(
            new GetOrderByIdQuery(created.Value, customerId));
        afterCancel.IsSuccess.ShouldBeTrue();
        afterCancel.Value.Status.ShouldBe(OrderStatus.Cancelled);
        afterCancel.Value.CancelledAt.ShouldNotBeNull();

        var again = await bus.InvokeAsync<Result>(
            new CancelOrderCommand(created.Value, "Changed my mind."));
        again.IsSuccess.ShouldBeFalse();

        await baskets.DeleteBasketAsync(basketKey);
    }

    [Fact]
    public async Task AddUnpublishedProduct_AsTheStorefrontBehaves_ShouldBeRejected()
    {
        var basketKey = $"journey-draft-{Guid.NewGuid():N}";

        await using var catalogDb = Fixture.CreateCatalogDbContext();
        var category = Category.Create("Books", "Reading material");
        catalogDb.Categories.Add(category);
        var draft = Product.Create("Draft", "Not sellable", "SKU-JRN-DRAFT", Money.Create(5m, "GEL"), category.Id);
        ((IHasDomainEvents)draft).ClearDomainEvents();
        catalogDb.Products.Add(draft);
        await catalogDb.SaveChangesAsync();

        await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(Fixture.RedisConnectionString);
        var priceSource = new CatalogPriceSource(new ProductRepository(catalogDb));

        // Same rejection the endpoint maps to 404: unpublished is
        // indistinguishable from nonexistent for a storefront crawler.
        await Should.ThrowAsync<BasketPricer.ProductNotSellableException>(
            BasketPricer.CreateLineAsync(priceSource, draft.Id, 1));
        await Should.ThrowAsync<BasketPricer.ProductNotSellableException>(
            BasketPricer.CreateLineAsync(priceSource, Guid.NewGuid(), 1));
    }
}
