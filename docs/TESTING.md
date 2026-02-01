# NetCommerce Testing Guide

> **Comprehensive testing strategy, patterns, and practices**

---

## Table of Contents

1. [Testing Philosophy](#testing-philosophy)
2. [Test Project Structure](#test-project-structure)
3. [Unit Testing](#unit-testing)
4. [Architecture Testing](#architecture-testing)
5. [Integration Testing](#integration-testing)
6. [Load Testing](#load-testing)
7. [Security Testing](#security-testing)
8. [Running Tests](#running-tests)
9. [Test Data Management](#test-data-management)
10. [Continuous Integration](#continuous-integration)

---

## Testing Philosophy

NetCommerce follows the **Testing Pyramid** with emphasis on:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TESTING PYRAMID                                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│                          ┌───────┐                                          │
│                         ╱         ╲                                         │
│                        ╱   E2E     ╲     Few, slow, high confidence        │
│                       ╱   (Manual)  ╲                                       │
│                      ╱───────────────╲                                      │
│                     ╱                  ╲                                    │
│                    ╱   Integration      ╲   Moderate count, real infra     │
│                   ╱     Tests            ╲                                  │
│                  ╱────────────────────────╲                                 │
│                 ╱                          ╲                                │
│                ╱   Architecture Tests       ╲   Fast, structural validation│
│               ╱                              ╲                              │
│              ╱────────────────────────────────╲                             │
│             ╱                                  ╲                            │
│            ╱         Unit Tests                 ╲   Many, fast, isolated   │
│           ╱                                      ╲                          │
│          ╱────────────────────────────────────────╲                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Testing Principles

| Principle | Description |
|-----------|-------------|
| **Fast feedback** | Unit tests run in < 5 seconds |
| **Deterministic** | Tests produce same result every time |
| **Isolated** | Tests don't depend on each other |
| **Domain-focused** | Test business logic, not implementation |
| **Architecture-enforced** | Clean Architecture validated by tests |

---

## Test Project Structure

```
tests/
├── NetCommerce.Domain.Tests/           # Unit tests for domain logic
│   ├── Catalog/                        # Product, Category tests
│   ├── Ordering/                       # Order aggregate tests
│   ├── Inventory/                      # Stock, Reservation tests
│   ├── Payments/                       # Transaction tests
│   ├── Finance/                        # Ledger, reconciliation tests
│   ├── Security/                       # Token, claims tests
│   ├── SharedKernel/                   # Result, Money, Entity tests
│   └── Fakers/                         # Bogus data generators
│
├── NetCommerce.Architecture.Tests/     # Clean Architecture validation
│   ├── LayerDependencyTests.cs         # Domain → App → Infra rules
│   ├── ForbiddenDependencyTests.cs     # Cross-module isolation
│   └── NamingConventionTests.cs        # Naming standards
│
├── NetCommerce.Integration.Tests/      # Real infrastructure tests
│   ├── Fixtures/                       # Testcontainers setup
│   ├── Catalog/                        # Product CRUD tests
│   ├── Ordering/                       # Order workflow tests
│   ├── Inventory/                      # Stock operations tests
│   ├── Payments/                       # Payment processing tests
│   ├── Wolverine/                      # Outbox, saga tests
│   ├── Security/                       # Auth, authorization tests
│   └── Performance/                    # Query performance tests
│
├── NetCommerce.LoadTests/              # NBomber load tests
│   ├── Scenarios/                      # Load test scenarios
│   │   ├── CheckoutFlowLoadTests.cs    # Full checkout flow
│   │   ├── StockConcurrencyTests.cs    # Inventory contention
│   │   ├── PS5LaunchLoadTests.cs       # Flash sale simulation
│   │   └── ContentionStressTests.cs    # Database contention
│   ├── Fixtures/                       # Test infrastructure
│   └── Assertions/                     # Custom assertions
│
└── NetCommerce.AppHost.Tests/          # Aspire configuration tests
```

---

## Unit Testing

### Testing Libraries

```csharp
// tests/NetCommerce.Domain.Tests/GlobalUsings.cs
global using Xunit;           // Test framework
global using NSubstitute;      // Mocking
global using Shouldly;         // Fluent assertions
global using Bogus;            // Fake data generation
```

### Domain Entity Tests

```csharp
/// <summary>
/// Tests for Order aggregate root.
/// Focus on business invariants and state transitions.
/// </summary>
public class OrderTests
{
    private readonly Faker _faker = new();

    [Fact]
    public void Create_WithValidData_ShouldCreateOrder()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var address = CreateValidAddress();

        // Act
        var order = Order.Create(customerId, address, "idempotency-key");

        // Assert
        order.CustomerId.ShouldBe(customerId);
        order.Status.ShouldBe(OrderStatus.Draft);
        order.OrderNumber.ShouldStartWith("ORD-");
    }

    [Fact]
    public void AddItem_WhenOrderSubmitted_ShouldFail()
    {
        // Arrange
        var order = CreateSubmittedOrder();
        var productId = Guid.NewGuid();

        // Act
        var result = order.AddItem(productId, "Product", Money.Create(100m), 1);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.InvalidState");
    }

    [Fact]
    public void Submit_WithEmptyItems_ShouldFail()
    {
        // Arrange
        var order = Order.Create(Guid.NewGuid(), CreateValidAddress(), "key");

        // Act
        var result = order.Submit();

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Order.EmptyItems");
    }

    [Fact]
    public void Submit_ShouldRaiseDomainEvent()
    {
        // Arrange
        var order = CreateOrderWithItems();

        // Act
        var result = order.Submit();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        order.DomainEvents.ShouldContain(e => e is OrderSubmittedDomainEvent);
    }
}
```

### Value Object Tests

```csharp
public class MoneyTests
{
    [Fact]
    public void Create_WithNegativeAmount_ShouldFail()
    {
        // Act
        var result = Money.Create(-100m);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Money.NegativeAmount");
    }

    [Fact]
    public void Add_SameCurrency_ShouldAddAmounts()
    {
        // Arrange
        var money1 = Money.Create(100m, "GEL").Value;
        var money2 = Money.Create(50m, "GEL").Value;

        // Act
        var result = money1.Add(money2);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(150m);
        result.Value.Currency.ShouldBe("GEL");
    }

    [Fact]
    public void Add_DifferentCurrency_ShouldFail()
    {
        // Arrange
        var gel = Money.Create(100m, "GEL").Value;
        var usd = Money.Create(50m, "USD").Value;

        // Act
        var result = gel.Add(usd);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Money.CurrencyMismatch");
    }
}
```

### Test Data Fakers

```csharp
/// <summary>
/// Bogus faker for generating realistic order test data.
/// </summary>
public sealed class OrderFaker : Faker<OrderTestData>
{
    public OrderFaker()
    {
        RuleFor(o => o.CustomerId, f => f.Random.Guid());
        RuleFor(o => o.OrderNumber, f => $"ORD-{f.Random.AlphaNumeric(12).ToUpper()}");
        RuleFor(o => o.Items, f => new OrderItemFaker().Generate(f.Random.Int(1, 5)));
        RuleFor(o => o.ShippingAddress, f => new AddressFaker().Generate());
    }
}

public sealed class AddressFaker : Faker<AddressTestData>
{
    public AddressFaker()
    {
        RuleFor(a => a.Street, f => f.Address.StreetAddress());
        RuleFor(a => a.City, f => f.Address.City());
        RuleFor(a => a.PostalCode, f => f.Address.ZipCode());
        RuleFor(a => a.Country, f => f.Address.Country());
    }
}
```

---

## Architecture Testing

### Clean Architecture Validation

Uses **NetArchTest** to enforce layer dependencies:

```csharp
/// <summary>
/// Architecture tests ensuring clean architecture principles.
/// These tests run on every build and prevent architectural drift.
/// </summary>
public class LayerDependencyTests
{
    // Assembly references
    private static readonly Assembly CatalogDomainAssembly = typeof(Product).Assembly;
    private static readonly Assembly CatalogApplicationAssembly = typeof(CreateProductCommand).Assembly;
    private static readonly Assembly CatalogInfrastructureAssembly = typeof(CatalogModule).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain should not depend on Application. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(CatalogDomainAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(CatalogApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("NetCommerce.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void SharedKernel_ShouldNotDependOn_AnyModule()
    {
        var result = Types.InAssembly(typeof(Entity<>).Assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "NetCommerce.Catalog",
                "NetCommerce.Ordering",
                "NetCommerce.Inventory",
                "NetCommerce.Payments")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

### Cross-Module Isolation

```csharp
public class ForbiddenDependencyTests
{
    [Fact]
    public void CatalogModule_ShouldNotDependOn_OrderingModule()
    {
        var result = Types.InNamespace("NetCommerce.Catalog")
            .Should()
            .NotHaveDependencyOn("NetCommerce.Ordering")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Modules must communicate only via integration events, not direct dependencies.");
    }

    [Fact]
    public void InventoryModule_ShouldNotDependOn_PaymentsModule()
    {
        var result = Types.InNamespace("NetCommerce.Inventory")
            .Should()
            .NotHaveDependencyOn("NetCommerce.Payments")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

### Naming Conventions

```csharp
public class NamingConventionTests
{
    [Fact]
    public void Commands_ShouldEndWithCommand()
    {
        var result = Types.InCurrentDomain()
            .That()
            .ImplementInterface(typeof(ICommand))
            .Should()
            .HaveNameEndingWith("Command")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void DomainEvents_ShouldEndWithDomainEvent()
    {
        var result = Types.InCurrentDomain()
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .HaveNameEndingWith("DomainEvent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

---

## Integration Testing

### Test Infrastructure

Uses **Testcontainers** for real PostgreSQL and Redis:

```csharp
/// <summary>
/// Integration test fixture with Testcontainers.
/// Provides real PostgreSQL and Redis for high-fidelity testing.
/// </summary>
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private RedisContainer _redisContainer = null!;
    private Respawner _respawner = null!;
    private IHost? _host;

    public string PostgresConnectionString => _postgresContainer.GetConnectionString();
    public string RedisConnectionString => _redisContainer.GetConnectionString();
    public IHost Host => _host!;

    public async Task InitializeAsync()
    {
        // Start containers in parallel
        _postgresContainer = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("netcommerce_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        _redisContainer = new RedisBuilder("redis:8-alpine")
            .Build();

        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _redisContainer.StartAsync());

        // Build host with Wolverine
        _host = await BuildTestHostAsync();

        // Initialize Respawner for database cleanup
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["catalog", "inventory", "ordering", "payments", "wolverine"],
            TablesToIgnore = ["__EFMigrationsHistory"],
            // Include Wolverine tables to prevent outbox leakage between tests
            TablesToInclude =
            [
                new Table("wolverine", "wolverine_incoming_envelopes"),
                new Table("wolverine", "wolverine_outgoing_envelopes"),
                new Table("wolverine", "wolverine_dead_letters")
            ]
        });
    }

    /// <summary>
    /// Reset database to clean state between tests.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _postgresContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
```

### Wolverine Message Tracking

```csharp
/// <summary>
/// Integration test with Wolverine tracked session.
/// Allows waiting for async message processing to complete.
/// </summary>
[Collection("Integration")]
public class OrderWorkflowTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;

    public OrderWorkflowTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrder_ShouldTriggerInventoryReservation()
    {
        // Arrange
        var bus = _fixture.Host.Services.GetRequiredService<IMessageBus>();
        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            CreateValidAddress(),
            [new OrderItemDto(Guid.NewGuid(), "Product", Money.Create(100m), 1)],
            Guid.NewGuid().ToString());

        // Act - Use tracked session to wait for all cascading messages
        var session = await _fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .InvokeMessageAndWaitAsync(command);

        // Assert - Verify integration event was published
        session.Sent.SingleMessage<OrderSubmittedIntegrationEvent>()
            .ShouldNotBeNull();

        // Assert - Verify inventory reservation was triggered
        session.Sent.SingleMessage<ReserveInventoryCommand>()
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task OrderFulfillmentSaga_ShouldCompleteSuccessfully()
    {
        // Arrange
        var bus = _fixture.Host.Services.GetRequiredService<IMessageBus>();
        var orderId = Guid.NewGuid();

        // Create test data
        await SeedProductWithStock(orderId);

        var command = new StartOrderFulfillmentCommand(
            orderId,
            Guid.NewGuid(),
            "ORD-TEST123",
            Money.Create(100m),
            [new OrderItemReservation(Guid.NewGuid(), 1)]);

        // Act - Track the entire saga
        var session = await _fixture.Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .InvokeMessageAndWaitAsync(command);

        // Assert - Saga completed without errors
        session.FindEnvelopesWithMessageType<SagaCompletedMessage>()
            .ShouldNotBeEmpty();

        // Verify saga state was deleted (completed successfully)
        var sagaState = await GetSagaStateAsync<OrderFulfillmentSaga>(orderId);
        sagaState.ShouldBeNull();
    }
}
```

### API Integration Tests

```csharp
[Collection("Integration")]
public class ProductEndpointTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    private readonly HttpClient _client;

    public ProductEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetProducts_ShouldReturnPaginatedList()
    {
        // Arrange
        await _fixture.SeedProductsAsync(25);

        // Act
        var response = await _client.GetAsync("/api/v1/products?page=1&pageSize=10");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<PagedResponse<ProductDto>>();
        content.ShouldNotBeNull();
        content.Items.Count.ShouldBe(10);
        content.TotalCount.ShouldBe(25);
        content.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateProduct_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _fixture.GetAdminToken());

        var request = new CreateProductRequest
        {
            Name = "Test Product",
            Description = "Test Description",
            Price = 99.99m,
            CategoryId = await _fixture.GetOrCreateCategoryAsync()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateProduct_WithoutAuth_ShouldReturn401()
    {
        // Arrange - No auth header
        var request = new CreateProductRequest { Name = "Test" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/products", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

---

## Load Testing

### NBomber Scenarios

```csharp
/// <summary>
/// Load tests for checkout flow under high concurrency.
/// Simulates flash sale with thousands of concurrent users.
/// </summary>
public class CheckoutFlowLoadTests
{
    [Fact(Skip = "Run manually - requires running API")]
    public void FlashSale_FullCheckoutFlow_ShouldMaintainConsistency()
    {
        const string apiBaseUrl = "http://localhost:5000";

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var scenario = Scenario.Create("flash_sale_checkout", async context =>
        {
            var idempotencyKey = Guid.NewGuid().ToString();

            // Step 1: Add to cart
            var addCartResponse = await AddToCart(httpClient, idempotencyKey);
            if (addCartResponse.IsError) return Response.Fail();

            // Step 2: Reserve stock (may get 409 Conflict = out of stock)
            var reserveResponse = await ReserveStock(httpClient, idempotencyKey);
            if (reserveResponse.StatusCode == "409")
                return Response.Ok("out_of_stock", 0);  // Expected in flash sale
            if (reserveResponse.IsError) return Response.Fail();

            // Step 3: Create order
            var orderResponse = await CreateOrder(httpClient, idempotencyKey);
            if (orderResponse.IsError) return Response.Fail();

            // Step 4: Process payment
            var paymentResponse = await ProcessPayment(httpClient, idempotencyKey);

            return paymentResponse.IsError
                ? Response.Fail()
                : Response.Ok();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(
                rate: 100,                          // 100 users per second
                interval: TimeSpan.FromSeconds(1),
                during: TimeSpan.FromMinutes(5)     // For 5 minutes
            ));

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .Run();

        // Assertions
        var scnStats = stats.ScenarioStats[0];

        // P99 latency should be under 1 second
        scnStats.Ok.Latency.Percent99.ShouldBeLessThan(1000);

        // Error rate should be under 1% (excluding expected 409s)
        var errorRate = (double)scnStats.Fail.Request.Count / scnStats.AllRequestCount;
        errorRate.ShouldBeLessThan(0.01);
    }
}
```

### Stock Contention Tests

```csharp
/// <summary>
/// Tests inventory under high contention.
/// Ensures no overselling even with concurrent requests.
/// </summary>
public class StockConcurrencyTests
{
    [Fact(Skip = "Run manually - requires running infrastructure")]
    public async Task ConcurrentReservations_ShouldNotOversell()
    {
        // Arrange: Product with 100 units
        const int initialStock = 100;
        const int concurrentRequests = 200;  // More requests than stock

        var productId = await SeedProductWithStock(initialStock);

        // Act: Fire 200 concurrent reservation requests
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => ReserveOneUnit(productId))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert: Exactly 100 should succeed, 100 should fail
        var successful = results.Count(r => r.IsSuccess);
        var failed = results.Count(r => r.IsFailure);

        successful.ShouldBe(initialStock);
        failed.ShouldBe(concurrentRequests - initialStock);

        // Verify final stock is 0, not negative
        var finalStock = await GetStockLevel(productId);
        finalStock.ShouldBe(0);
    }
}
```

---

## Security Testing

### Authorization Tests

```csharp
[Collection("Integration")]
public class CrossTenantDataLeakageTests
{
    [Fact]
    public async Task Customer_CannotAccessOtherTenantOrders()
    {
        // Arrange: Create orders for two different tenants
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var orderInTenantA = await CreateOrderForTenant(tenantA);
        var tokenForTenantB = GetTokenForTenant(tenantB);

        var client = _fixture.CreateAuthenticatedClient(tokenForTenantB);

        // Act: Tenant B tries to access Tenant A's order
        var response = await client.GetAsync($"/api/v1/orders/{orderInTenantA.Id}");

        // Assert: Should be 404 (not 403, to prevent enumeration)
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TokenIntrospection_ShouldBlockRevokedTokens()
    {
        // Arrange: Get valid token, then revoke it
        var token = await GetValidToken();
        await RevokeToken(token);

        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Act: Try to access protected endpoint
        var response = await client.GetAsync("/api/v1/orders");

        // Assert: Should be 401 (token revoked)
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

### Idempotency Tests

```csharp
public class IdempotencyTests
{
    [Fact]
    public async Task SameIdempotencyKey_ShouldReturnSameResult()
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var client = _fixture.CreateAuthenticatedClient();

        var request = new CreateOrderRequest { /* ... */ };

        // First request
        var response1 = await PostWithIdempotencyKey(client, "/api/v1/orders", request, idempotencyKey);
        var orderId1 = await response1.Content.ReadFromJsonAsync<Guid>();

        // Second request with same key
        var response2 = await PostWithIdempotencyKey(client, "/api/v1/orders", request, idempotencyKey);
        var orderId2 = await response2.Content.ReadFromJsonAsync<Guid>();

        // Should return same order ID
        orderId1.ShouldBe(orderId2);

        // Only one order should exist
        var orders = await GetAllOrders();
        orders.Count.ShouldBe(1);
    }
}
```

---

## Running Tests

### Command Line

```powershell
# Run all tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Run specific test project
dotnet test tests/NetCommerce.Domain.Tests --nologo

# Run tests with filter
dotnet test --filter "FullyQualifiedName~OrderTests"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run architecture tests only (fast)
dotnet test tests/NetCommerce.Architecture.Tests --nologo

# Run integration tests (requires Docker)
dotnet test tests/NetCommerce.Integration.Tests --nologo
```

### VS Code Tasks

```json
{
  "label": "dotnet-test-sln",
  "type": "shell",
  "command": "dotnet",
  "args": ["test", "NetCommerce.slnx", "-v", "minimal", "--nologo"],
  "group": "build"
}
```

### Prerequisites

| Test Type | Requirements |
|-----------|--------------|
| Unit Tests | None |
| Architecture Tests | None |
| Integration Tests | Docker Desktop |
| Load Tests | Running API + Infrastructure |

---

## Test Data Management

### Respawn for Database Cleanup

```csharp
// Reset database between tests (preserves schema, deletes data)
await _respawner.ResetAsync(connection);
```

### Bogus for Fake Data

```csharp
var faker = new Faker<Product>()
    .RuleFor(p => p.Name, f => f.Commerce.ProductName())
    .RuleFor(p => p.Price, f => f.Random.Decimal(10, 1000))
    .RuleFor(p => p.Description, f => f.Lorem.Paragraph());

var products = faker.Generate(100);
```

---

## Continuous Integration

### GitHub Actions

```yaml
name: Tests

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet test tests/NetCommerce.Domain.Tests --nologo
      - run: dotnet test tests/NetCommerce.Architecture.Tests --nologo

  integration-tests:
    runs-on: ubuntu-latest
    services:
      docker:
        image: docker:dind
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet test tests/NetCommerce.Integration.Tests --nologo
```

### Test Coverage Goals

| Category | Target |
|----------|--------|
| Domain Logic | 90%+ |
| Application Handlers | 80%+ |
| Infrastructure | 70%+ |
| Overall | 80%+ |

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Platform Team
