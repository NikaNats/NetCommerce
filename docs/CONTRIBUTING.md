# Contributing

Guidelines for contributing to NetCommerce. Follow these conventions to maintain codebase consistency and pass all architecture tests.

## Development Workflow

1. Create a feature branch from `main`
2. Implement changes following the conventions below
3. Run all tests: `dotnet test NetCommerce.slnx -v minimal --nologo`
4. Verify architecture boundaries: `dotnet test tests/NetCommerce.Architecture.Tests --nologo`
5. Submit a pull request

## Project Conventions

### Module Structure

Every bounded context follows this three-layer structure:

```
src/{Module}/
├── {Module}.Application/     # Commands, queries, Wolverine handlers
├── {Module}.Domain/          # Aggregates, entities, value objects
├── {Module}.Infrastructure/  # EF Core, external service adapters
```

- **Domain** depends on nothing (only `NetCommerce.Kernel.Core`)
- **Application** depends on Domain and `NetCommerce.Kernel.Application`
- **Infrastructure** depends on Application and `NetCommerce.Kernel.EfCore`

These boundaries are enforced by architecture tests in `NetCommerce.Architecture.Tests`.

### Strongly Typed IDs

All entity IDs implement `IStronglyTypedId<T>` as a `readonly record struct`:

```csharp
public readonly record struct ProductId(Guid Value) : IStronglyTypedId<ProductId>
{
    public static ProductId Create(Guid value) => new(value);
    public static ProductId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));
    public static bool TryParse(string? s, IFormatProvider? provider, out ProductId result)
    {
        if (Guid.TryParse(s, provider, out var guid))
        {
            result = new(guid);
            return true;
        }
        result = default;
        return false;
    }
}
```

EF Core converters register automatically via `StronglyTypedIdConvention` in `BaseDbContext`.

### Result Pattern

Return `Result<T>` from all command handlers. Never throw exceptions for business errors:

```csharp
// Correct
public static Result<Guid> Handle(CreateProductCommand command)
{
    if (string.IsNullOrWhiteSpace(command.Title))
        return Result.Failure<Guid>(Error.Validation("Title is required"));

    var product = Product.Create(command.Title, command.Price);
    return Result.Success(product.Id.Value);
}

// Incorrect — do NOT throw for business logic
public static Guid Handle(CreateProductCommand command)
{
    if (string.IsNullOrWhiteSpace(command.Title))
        throw new ArgumentException("Title is required"); // ❌
    ...
}
```

### Wolverine Message Handlers

Use static handler classes with the `[WolverineHandler]` attribute. Return values become cascading messages published via the transactional outbox:

```csharp
[WolverineHandler]
public static class OrderSubmittedHandler
{
    public static InventoryReserved Handle(
        OrderSubmittedIntegrationEvent @event,
        ILogger logger)
    {
        logger.LogInformation("Processing order {OrderId}", @event.OrderId);
        return new InventoryReserved(@event.OrderId, ...);
    }
}
```

Handler discovery scans specific assemblies registered in `Program.cs`. New handler assemblies must be added to the Wolverine configuration.

### Domain Events vs Integration Events

| Type | Scope | Location | Transport |
|---|---|---|---|
| Domain Events | Internal to a module | `{Module}.Domain/Events/` | In-process via `RaiseDomainEvent()` |
| Integration Events | Cross-module | `src/Domain.Shared/Events/` | Wolverine transactional outbox |

Domain events are raised on aggregate roots:

```csharp
public void Publish()
{
    Status = ProductStatus.Published;
    RaiseDomainEvent(new ProductPublishedEvent(Id));
}
```

Integration events are published via Wolverine and consumed by other modules:

```csharp
public record OrderSubmittedIntegrationEvent(Guid OrderId, string OrderNumber, Guid CustomerId);
```

### EF Core DbContext

Each module has its own `DbContext` inheriting `BaseDbContext`, with an isolated schema:

```csharp
public class CatalogDbContext : BaseDbContext
{
    public const string Schema = "catalog";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
```

### Value Objects

Inherit from `ValueObject` and override `GetEqualityComponents()`:

```csharp
public sealed class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string PostalCode { get; }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return PostalCode;
    }
}
```

The `Money` value object defaults to **GEL** currency:

```csharp
Money.Create(100m);           // 100 GEL
Money.Create(50m, "USD");     // 50 USD
```

### Endpoint Registration

Endpoints implement `IEndpoint` (static) or `IEndpointGroup` and are registered explicitly in `MapNetCommerceEndpoints()` for Native AOT compatibility:

```csharp
public class ProductEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var group = app.MapGroup("/api/v{version:apiVersion}/products")
            .WithApiVersionSet(versionSet);

        group.MapGet("/{id:guid}", GetById).AllowAnonymous();
        group.MapPost("/", Create).RequireAuthorization("VendorOnly");
        // ...
    }
}
```

### JSON Source Generation

All request/response types must be registered in `ApiJsonContext` for Native AOT:

```csharp
[JsonSerializable(typeof(CreateProductCommand))]
[JsonSerializable(typeof(ProductDto))]
[JsonSerializable(typeof(PaginatedResponse<ProductDto>))]
public partial class ApiJsonContext : JsonSerializerContext { }
```

Forgetting to register a type causes runtime serialization failures in AOT builds.

## Code Quality

### Analysis Rules

The solution enforces:

- `AnalysisLevel: latest-all` with all analyzers enabled
- `TreatWarningsAsErrors` in Release and CI builds
- `EnforceCodeStyleInBuild: true`
- Nullable reference types enabled globally

Suppressed warnings (with justification):

| Warning | Reason |
|---|---|
| `CS1591` | XML doc comments not required on all public members |
| `CA1014` | Assembly-level `CLSCompliant` attribute not needed |
| `NETSDK1210` | Aspire implicit usings conflict |
| `NU1608` | Transitive dependency version resolution |

### Naming Conventions

Architecture tests enforce these naming rules:

- Command handler classes end with `Handler`
- Integration events end with `IntegrationEvent` or appear in `Events` namespace
- DbContext classes end with `DbContext`
- Repository classes end with `Repository`

### Architecture Boundaries

The `NetCommerce.Architecture.Tests` project uses NetArchTest to validate:

- **Forbidden dependencies** — Domain layers never reference Infrastructure or Application
- **Layer dependencies** — Application does not reference UI/API layer
- **Naming conventions** — consistent naming across all modules

Run before every PR:

```powershell
dotnet test tests/NetCommerce.Architecture.Tests --nologo
```

## Testing Guidelines

### Test Categories

| Project | Type | Infrastructure |
|---|---|---|
| `NetCommerce.Domain.Tests` | Unit tests | In-memory, no external deps |
| `NetCommerce.Architecture.Tests` | Architecture tests | Static analysis, no runtime |
| `NetCommerce.Integration.Tests` | Integration tests | Testcontainers (PostgreSQL, Redis) |
| `NetCommerce.LoadTests` | Load/stress tests | NBomber, requires PostgreSQL |
| `NetCommerce.AppHost.Tests` | Topology tests | Aspire hosting |

### Writing Unit Tests

Use xUnit, FluentAssertions, NSubstitute, and Bogus:

```csharp
public class ProductTests
{
    [Fact]
    public void Create_WithValidData_ReturnsProduct()
    {
        var product = Product.Create("Widget", Money.Create(29.99m));

        product.Title.Should().Be("Widget");
        product.Price.Amount.Should().Be(29.99m);
        product.Price.Currency.Should().Be("GEL");
    }
}
```

### Writing Integration Tests

Use `IntegrationTestFixture` with Testcontainers and Respawn:

```csharp
public class OrderRepositoryTests(IntegrationTestFixture fixture)
    : IClassFixture<IntegrationTestFixture>
{
    [Fact]
    public async Task SaveAndRetrieve_Order_Succeeds()
    {
        // Arrange — fixture provides real PostgreSQL + Redis
        await using var scope = fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        // Act & Assert
        ...
    }
}
```

Respawn resets the database between tests for isolation.

## Commit Conventions

Use conventional commit messages:

```
feat(catalog): add product image management endpoints
fix(inventory): prevent partial reservation leak on concurrent requests
refactor(kernel): migrate Result pattern to Kernel.Core
test(ordering): add saga compensation sad-path tests
docs(api): update webhook reference with dispute events
```

## Pull Request Checklist

- [ ] All tests pass: `dotnet test NetCommerce.slnx -v minimal --nologo`
- [ ] Architecture tests pass: `dotnet test tests/NetCommerce.Architecture.Tests --nologo`
- [ ] No new analyzer warnings in Release build
- [ ] New types registered in `ApiJsonContext` if exposed via API
- [ ] Integration events added to `src/Domain.Shared/Events/` if cross-module
- [ ] EF Core migration added if schema changes
- [ ] Documentation updated for public API changes

## Related Documentation

- [Architecture](ARCHITECTURE.md) — design principles and module boundaries
- [Testing](TESTING.md) — full test strategy and fixture setup
- [API Reference](API_REFERENCE.md) — endpoint documentation standards
- [Native AOT](NATIVE_AOT_VERIFICATION.md) — AOT compatibility verification
