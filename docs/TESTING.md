# Testing

Complete test strategy, infrastructure, and guidelines for NetCommerce's 608-test suite across 5 test projects.

## Test Projects

| Project | Type | Count | Infrastructure | Framework |
|---|---|---|---|---|
| `NetCommerce.Domain.Tests` | Unit | ~536 | In-memory, no external deps | xUnit, FluentAssertions, NSubstitute, Bogus |
| `NetCommerce.Architecture.Tests` | Architecture | ~36 | Static analysis | xUnit, NetArchTest |
| `NetCommerce.Integration.Tests` | Integration | ~36+ | Testcontainers (PostgreSQL, Redis) | xUnit, Testcontainers, Respawn |
| `NetCommerce.LoadTests` | Load/Stress | ~7 | NBomber, PostgreSQL | NBomber, xUnit |
| `NetCommerce.AppHost.Tests` | Topology | ~1 | Aspire hosting | xUnit, Aspire.Hosting.Testing |

## Running Tests

```powershell
# All tests
dotnet test NetCommerce.slnx -v minimal --nologo

# Specific project
dotnet test tests/NetCommerce.Domain.Tests --nologo
dotnet test tests/NetCommerce.Architecture.Tests --nologo
dotnet test tests/NetCommerce.Integration.Tests --nologo

# Filter by test name
dotnet test --filter "FullyQualifiedName~OrderFulfillmentSaga" --nologo

# With detailed output
dotnet test NetCommerce.slnx -v detailed --nologo
```

## Unit Tests (Domain.Tests)

### Test Categories

#### Auditing
- `AuditMiddlewareTests` — audit trail middleware behavior
- `AuditRepositoryTests` — audit entry persistence

#### Catalog
- `ProductTests` — product aggregate creation, publish, archive, price update
- `CachedProductRepositorySecurityTests` — cache isolation and security

#### Finance
- `FinancialHardeningTests` — financial flow integrity
- `ReconciliationEngineTests` — reconciliation algorithm (mock PSP)
- `ReconciliationSessionTests` — session state management
- `TriplePassPricingPropertyTests` — property-based pricing tests (FsCheck)

#### Inventory
- `StockTests` — stock aggregate reservation, confirmation, release
- `ReservationCleanupJobTests` — cleanup job logic

#### Ordering
- `OrderTests` — order aggregate creation, cancellation
- `OrderFulfillmentSagaTests` — saga state transitions (happy + sad paths)
- `SagaCompensationTests` — compensation flow verification
- `GracePeriodTests` — grace period timeout behavior
- `OrderingMetricsTests` — metrics emission
- `OrderNotificationHandlerTests` — SignalR notification dispatch
- `SimplePromotionEngineTests` — promotion application
- `TriplePassPricingTests` — pricing algorithm verification
- `NotificationInfrastructureTests` — notification infrastructure

#### Payments
- `ProcessExternalPaymentConfirmationHandlerTests` — webhook command handler
- `StripePaymentGatewayResilienceTests` — Polly retry/circuit breaker
- `StripePaymentGatewayWebhookFirstTests` — webhook-first pattern
- `PaymentReconciliationJobTests` — payment reconciliation

#### Privacy
- `PiiIsolationTests` — PII vault isolation
- `PiiTaintAnalysisTests` — PII data flow taint analysis

#### Security
- `AdminElevatedAuthorizationTests` — elevated admin auth
- `BffAuthEndpointTests` — BFF auth endpoint behavior
- `KeycloakRolesClaimsTransformationTests` — Keycloak claims mapping
- `KeycloakTokenProxyTests` — token proxy behavior
- `RateLimitingTests` — rate limiter configuration
- `ResourceOwnerAuthorizationTests` — ROPC rejection
- `TokenExchangeDelegatingHandlerTests` — token exchange delegation
- `TokenIntrospectionMiddlewareTests` — zero-trust introspection
- `ZeroTrustAuthOptionsTests` — auth configuration validation

#### Shared Kernel
- `CrossCurrencyPropertyTests` — Money cross-currency operations
- `PriceBreakdownTests` — price breakdown calculations
- `ValueObjectTests` — value object equality

#### Shipping
- `ShippingModuleTests` — shipment lifecycle
- `ShippingModuleTests_Extended` — extended scenarios

#### Workers
- `ReservationExpiryTests` — reservation expiry logic
- `TimeOperationsTests` — time-based operations

### Test Patterns

**Arrange-Act-Assert** with FluentAssertions:

```csharp
[Fact]
public void Product_Create_WithValidData_SetsProperties()
{
    // Arrange
    var title = "Widget";
    var price = Money.Create(29.99m);

    // Act
    var product = Product.Create(title, price, "WDG-001");

    // Assert
    product.Title.Should().Be("Widget");
    product.Price.Amount.Should().Be(29.99m);
    product.Status.Should().Be(ProductStatus.Draft);
}
```

**Mocking with NSubstitute:**

```csharp
var repository = Substitute.For<IProductRepository>();
repository.GetByIdAsync(Arg.Any<ProductId>())
    .Returns(Product.Create("Test", Money.Create(10m)));
```

**Test Data with Bogus:**

```csharp
var faker = new Faker<CreateProductCommand>()
    .RuleFor(x => x.Title, f => f.Commerce.ProductName())
    .RuleFor(x => x.Price, f => Money.Create(f.Finance.Amount()));
```

**Property-Based Testing with FsCheck:**

```csharp
[Property]
public Property Money_Add_IsCommutative(decimal a, decimal b)
{
    var m1 = Money.Create(Math.Abs(a));
    var m2 = Money.Create(Math.Abs(b));
    return (m1.Add(m2).Amount == m2.Add(m1).Amount).ToProperty();
}
```

## Architecture Tests

Architecture tests validate Clean Architecture boundaries using NetArchTest:

### ForbiddenDependencyTests

Ensures domain layers never reference infrastructure:

```csharp
[Fact]
public void Domain_ShouldNotDependOn_Infrastructure()
{
    var result = Types.InAssembly(domainAssembly)
        .ShouldNot()
        .HaveDependencyOn("Microsoft.EntityFrameworkCore")
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}
```

### LayerDependencyTests

Validates layer dependency rules:
- Domain → only Kernel.Core
- Application → Domain + Kernel.Application
- Infrastructure → Application + Kernel.EfCore

### NamingConventionTests

Enforces naming standards:
- Handlers end with `Handler`
- DbContexts end with `DbContext`
- Repositories end with `Repository`

## Integration Tests

### Test Infrastructure

Integration tests use **Testcontainers** for real PostgreSQL and Redis instances, and **Respawn** for database cleanup between tests.

#### IntegrationTestFixture

```csharp
public class IntegrationTestFixture : IAsyncLifetime
{
    // Provisions:
    // - PostgreSQL container (real database)
    // - Redis container (real cache/basket)
    // - EF Core migrations (all 6 schemas)
    // - Wolverine messaging (in-memory)
    // - Service registrations (repositories, handlers)
    
    public async Task InitializeAsync()
    {
        // Start containers
        // Run migrations
        // Register services
    }

    public async Task DisposeAsync()
    {
        // Stop and remove containers
    }

    public AsyncServiceScope CreateScope()
        => _serviceProvider.CreateAsyncScope();
}
```

Respawn resets all database tables between tests by truncating data while preserving schema.

### Integration Test Categories

#### API
- `ZeroTrustAuthenticationIntegrationTests` — end-to-end auth flow

#### Catalog
- `CatalogRepositoryTests` — EF Core product persistence
- `ProductCacheInvalidationHandlerTests` — cache eviction on changes

#### Chaos Engineering
- `ChaosEngineeringTests` — fault injection scenarios
- `DbContextChaosInterceptorTests` — database fault simulation
- `FinancialIntegrationTests` — financial flow under chaos
- `RedisKillDrillTests` — Redis failure scenarios

#### Edge Cases
- `ClockSkewTests` — time synchronization issues
- `InventorySoftLockLeakTests` — reservation leak detection
- `MultiCurrencyRoundingTests` — currency rounding edge cases
- `PartitionSkewToasterGuardTests` — partition skew protection
- `SagaClockSkewTests` — saga timing issues

#### Finance
- `GhostChargeRecoveryTests` — ghost charge detection and recovery
- `ReconciliationIntegrationTests` — end-to-end reconciliation

#### Infrastructure
- `KeycloakDowntimeTests` — auth provider failure
- `PostgresConnectionPoolExhaustionTests` — connection pool limits
- `RedisKillSwitchFailClosedTests` — Redis kill switch behavior
- `RedisOutageResilienceTests` — Redis outage handling
- `WalExhaustionStressTests` — WAL size limits

#### Inventory
- `ConcurrentInventoryStressTests` — concurrent reservation under contention
- `InventoryRepositoryTests` — stock persistence
- `ReservationCleanupJobTests` — cleanup with real database

#### Observability
- `CorrelationIdPropagationTests` — correlation ID across requests

#### Ordering
- `GracePeriodIntegrationTests` — grace period with real timers
- `OrderFulfillmentSagaE2ETests` — full saga lifecycle (3 tests)
- `OrderFulfillmentSagaIntegrationTests` — saga with real persistence (13 tests)
- `OrderFulfillmentSagaSadPathTests` — compensation flows (6 tests)

#### Payments
- `PaymentWebhookContractTests` — Stripe event → command mapping (10 tests)
- `PaymentWebhookTests` — webhook with real database
- `WebhookRaceConditionTests` — concurrent webhook delivery
- `StripeWebhookDelayedDeliveryTests` — late webhook arrival

#### Performance
- `LargeCartSerializationTests` — large basket serialization
- `MeilisearchSyncLagTests` — search index sync latency

#### Resilience
- `DeadLetterQueueReplayTests` — DLQ replay mechanism
- `OutboxPoisonMessageIsolationTests` — poison message handling
- `SagaBlueGreenCompatibilityTests` — deployment compatibility
- `WolverineOutboxBloatTests` — outbox table growth

#### Security
- `CrossTenantDataLeakageAuditTests` — tenant isolation verification
- `IdempotencyKeySecurityTests` — idempotency key validation
- `IdempotencyTenantJackingTests` — cross-tenant key reuse prevention
- `InternalBusPrivilegeEscalationTests` — message bus security
- `PiiScrubbingAuditTests` — PII data scrubbing

#### Wolverine
- `TransactionalOutboxTests` — outbox reliability
- `WolverineTrackedSessionTests` — tracked session behavior

## Load Tests

Load tests use **NBomber** for high-concurrency scenarios:

- `CheckoutFlowLoadTests` — full checkout pipeline under load
- `ContentionStressTests` — inventory contention analysis
- `PartitionedStockHandlerTests` — partitioned stock handler performance
- `PS5LaunchLoadTests` — flash sale simulation (extreme contention)
- `RedisKillScriptTests` — Redis failure under load
- `StockConcurrencyTests` — concurrent stock operations
- `ToasterGuardStressTests` — circuit breaker behavior

### Load Test Infrastructure

- `PostgresTestFixture` — PostgreSQL setup for load tests
- `ContentionMetrics` — contention measurement instrumentation
- `ContentionAssertions` — assertion helpers for contention thresholds
- `SagaLeakAssertions` — saga leak detection assertions

## Test Dependencies

| Package | Version | Purpose |
|---|---|---|
| xUnit | 2.9.3 | Test framework |
| FluentAssertions | 7.2.0 | Assertion library |
| NSubstitute | 5.3.0 | Mocking framework |
| Bogus | 35.6.5 | Test data generation |
| FsCheck | 3.2.0 | Property-based testing |
| FsCheck.Xunit | 3.2.0 | FsCheck xUnit integration |
| NetArchTest.Rules | 1.3.2 | Architecture rule validation |
| Testcontainers.PostgreSql | 4.10.0 | PostgreSQL test container |
| Testcontainers.Redis | 4.10.0 | Redis test container |
| Respawn | 7.0.0 | Database cleanup |
| WireMock.Net | 1.22.0 | HTTP mock server |
| NBomber | 6.1.2 | Load testing framework |
| NBomber.Http | 6.1.0 | HTTP load testing |
| Shouldly | 4.3.0 | Alternative assertion library |

## CI Integration

Tests run as part of the CI pipeline:

```powershell
dotnet test NetCommerce.slnx -v minimal --nologo
```

**TreatWarningsAsErrors** is enabled in CI builds, ensuring no suppressible warnings pass.

Integration tests require Docker to run Testcontainers. Load tests are typically excluded from CI and run on dedicated infrastructure.

## Related Documentation

- [Contributing](CONTRIBUTING.md) — writing tests for new features
- [Architecture](ARCHITECTURE.md) — module boundaries validated by tests
- [Troubleshooting](TROUBLESHOOTING.md) — common test failures
