# Contributing to NetCommerce

> **Guidelines for contributing to the NetCommerce platform**

---

## Table of Contents

1. [Code of Conduct](#code-of-conduct)
2. [Getting Started](#getting-started)
3. [Development Workflow](#development-workflow)
4. [Code Standards](#code-standards)
5. [Architecture Guidelines](#architecture-guidelines)
6. [Pull Request Process](#pull-request-process)
7. [Commit Messages](#commit-messages)
8. [Testing Requirements](#testing-requirements)
9. [Documentation](#documentation)
10. [Review Process](#review-process)

---

## Code of Conduct

- Be respectful and inclusive
- Provide constructive feedback
- Focus on the code, not the person
- Help others learn and grow

---

## Getting Started

### Prerequisites

```powershell
# Required
- .NET 10 SDK (Preview)
- Docker Desktop
- Git

# Recommended
- Visual Studio Code with C# Dev Kit
- JetBrains Rider 2025.1+
```

### Setup

```powershell
# Clone the repository
git clone https://github.com/your-org/NetCommerce.git
cd NetCommerce

# Restore dependencies
dotnet restore

# Run tests to verify setup
dotnet test NetCommerce.slnx --nologo

# Start the application
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

### IDE Configuration

#### VS Code

Install recommended extensions:
- C# Dev Kit
- .NET Aspire
- EditorConfig
- GitLens

#### JetBrains Rider

Import the shared settings from `.editorconfig`.

---

## Development Workflow

### Branch Strategy

```
main                    Production-ready code
├── develop             Integration branch
│   ├── feature/xxx     New features
│   ├── bugfix/xxx      Bug fixes
│   ├── refactor/xxx    Code improvements
│   └── docs/xxx        Documentation
└── release/vX.Y        Release candidates
```

### Creating a Feature Branch

```powershell
# Update develop
git checkout develop
git pull origin develop

# Create feature branch
git checkout -b feature/add-wishlist-module

# Make changes, commit, push
git add .
git commit -m "feat(wishlist): add wishlist aggregate root"
git push -u origin feature/add-wishlist-module
```

### Local Development Cycle

```powershell
# 1. Start infrastructure
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj

# 2. Make changes

# 3. Run tests
dotnet test NetCommerce.slnx --nologo

# 4. Verify architecture
dotnet test tests/NetCommerce.Architecture.Tests --nologo

# 5. Check for warnings (treated as errors in CI)
dotnet build -c Release
```

---

## Code Standards

### C# Conventions

```csharp
// ✅ DO: Use file-scoped namespaces
namespace NetCommerce.Ordering.Domain.Orders;

// ✅ DO: Use primary constructors for simple types
public sealed class OrderService(IOrderRepository repository, ILogger<OrderService> logger)
{
    // ...
}

// ✅ DO: Use records for DTOs and value objects
public sealed record OrderDto(Guid Id, string OrderNumber, Money Total);

// ✅ DO: Use expression-bodied members where appropriate
public Money Total => Items.Sum(i => i.Subtotal);

// ✅ DO: Use nullable reference types
public string? Description { get; private set; }

// ❌ DON'T: Use var when type isn't obvious
var x = GetSomething();  // What type is this?

// ✅ DO: Be explicit when type isn't obvious
Order order = GetOrder();
```

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `OrderRepository` |
| Interfaces | IPascalCase | `IOrderRepository` |
| Methods | PascalCase | `GetByIdAsync` |
| Properties | PascalCase | `OrderNumber` |
| Private fields | _camelCase | `_repository` |
| Parameters | camelCase | `orderId` |
| Constants | PascalCase | `MaxRetries` |
| Generic types | T-prefix | `TEntity`, `TId` |

### File Organization

```csharp
// Order: Using directives → Namespace → Type

using System;
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Ordering.Domain.Orders;

/// <summary>
/// XML documentation for public types.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    // 1. Constants
    private const int MaxItems = 100;

    // 2. Static members
    public static Order Create(...) { }

    // 3. Private fields
    private readonly List<OrderItem> _items = [];

    // 4. Constructor (private for aggregates)
    private Order() { }

    // 5. Properties
    public OrderNumber OrderNumber { get; private set; } = null!;

    // 6. Public methods
    public Result AddItem(...) { }

    // 7. Private methods
    private void ValidateState() { }
}
```

### Result Pattern Usage

```csharp
// ✅ DO: Return Result<T> from domain operations
public Result<Order> AddItem(ProductId productId, Money price, int quantity)
{
    if (Status != OrderStatus.Draft)
        return Result.Failure<Order>(Error.Validation("Order.InvalidState",
            "Cannot add items to a submitted order"));

    if (_items.Count >= MaxItems)
        return Result.Failure<Order>(Error.Validation("Order.TooManyItems",
            $"Order cannot have more than {MaxItems} items"));

    _items.Add(new OrderItem(productId, price, quantity));
    return Result.Success(this);
}

// ✅ DO: Chain results with Bind/Map
public Result<OrderDto> GetOrderDto(Guid orderId)
{
    return GetOrderById(orderId)
        .Map(order => order.ToDto());
}

// ❌ DON'T: Throw exceptions for business logic errors
public void AddItem(...)
{
    if (Status != OrderStatus.Draft)
        throw new InvalidOperationException("..."); // Don't do this
}
```

### Domain Event Pattern

```csharp
// ✅ DO: Raise domain events from aggregates
public Result Submit()
{
    if (!_items.Any())
        return Result.Failure(Error.Validation("Order.Empty", "Order has no items"));

    Status = OrderStatus.Submitted;
    SubmittedAt = DateTime.UtcNow;

    // Raise event for side effects
    RaiseDomainEvent(new OrderSubmittedDomainEvent(Id, OrderNumber, CustomerId));

    return Result.Success();
}

// ✅ DO: Name events in past tense
public sealed record OrderSubmittedDomainEvent(
    OrderId OrderId,
    string OrderNumber,
    Guid CustomerId) : IDomainEvent;
```

---

## Architecture Guidelines

### Module Structure

Every bounded context follows this structure:

```
src/{Module}/
├── {Module}.Domain/              # Core business logic
│   ├── {Aggregate}/              # Aggregate folder
│   │   ├── {Aggregate}.cs        # Aggregate root
│   │   ├── {Aggregate}Id.cs      # Strongly typed ID
│   │   ├── {Entity}.cs           # Child entities
│   │   ├── I{Aggregate}Repository.cs  # Repository interface
│   │   └── Events/               # Domain events
│   └── ValueObjects/             # Shared value objects
│
├── {Module}.Application/         # Use cases
│   ├── {Feature}/
│   │   ├── Commands/             # State-changing operations
│   │   └── Queries/              # Read operations
│   └── Services/                 # Application services
│
└── {Module}.Infrastructure/      # External concerns
    ├── Persistence/              # EF Core configuration
    ├── Handlers/                 # Wolverine handlers
    └── Services/                 # External service implementations
```

### Layer Dependencies

```
┌─────────────────────────────────────────────────────────────────┐
│                    ALLOWED DEPENDENCIES                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Domain Layer (innermost):                                      │
│  ├── Depends on: Kernel.Core only                              │
│  ├── NO dependencies on Application or Infrastructure          │
│  └── NO dependencies on other modules                          │
│                                                                 │
│  Application Layer:                                             │
│  ├── Depends on: Domain, Kernel.Core, Kernel.Application       │
│  ├── NO dependencies on Infrastructure                         │
│  └── NO dependencies on other modules                          │
│                                                                 │
│  Infrastructure Layer (outermost):                              │
│  ├── Depends on: Domain, Application, Kernel.*                 │
│  ├── Implements interfaces from Domain/Application             │
│  └── NO dependencies on other modules' Infrastructure          │
│                                                                 │
│  Cross-Module Communication:                                     │
│  └── ONLY via Domain.Shared integration events                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Adding a New Module

1. **Create projects:**
```powershell
dotnet new classlib -n NetCommerce.Wishlist.Domain -o src/Wishlist/Wishlist.Domain
dotnet new classlib -n NetCommerce.Wishlist.Application -o src/Wishlist/Wishlist.Application
dotnet new classlib -n NetCommerce.Wishlist.Infrastructure -o src/Wishlist/Wishlist.Infrastructure
```

2. **Add to solution:**
```powershell
dotnet sln add src/Wishlist/Wishlist.Domain
dotnet sln add src/Wishlist/Wishlist.Application
dotnet sln add src/Wishlist/Wishlist.Infrastructure
```

3. **Add project references following layer rules**

4. **Create database in AppHost:**
```csharp
var wishlistDb = postgres.AddDatabase("WishlistDb", "wishlist");
```

5. **Add architecture tests for new module**

---

## Pull Request Process

### Before Creating PR

- [ ] All tests pass locally
- [ ] No compiler warnings (treated as errors in Release)
- [ ] Architecture tests pass
- [ ] Code follows project conventions
- [ ] Documentation updated if needed

### PR Title Format

```
type(scope): short description

Examples:
feat(ordering): add order cancellation within grace period
fix(inventory): prevent overselling under high concurrency
refactor(payments): extract payment gateway abstraction
docs(api): add authentication examples to API reference
test(catalog): add product search integration tests
```

### PR Description Template

```markdown
## Summary
Brief description of changes.

## Changes
- Added X
- Modified Y
- Removed Z

## Type
- [ ] Feature
- [ ] Bug fix
- [ ] Refactor
- [ ] Documentation
- [ ] Tests

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manually tested locally

## Breaking Changes
None / Description of breaking changes

## Related Issues
Closes #123
```

### Review Checklist

Reviewers will verify:

- [ ] Code follows architecture guidelines
- [ ] Result pattern used (no business exceptions)
- [ ] Domain events raised appropriately
- [ ] Tests cover happy path and error cases
- [ ] No security vulnerabilities
- [ ] Performance considerations addressed
- [ ] Documentation updated

---

## Commit Messages

### Conventional Commits

```
type(scope): description

[optional body]

[optional footer]
```

### Types

| Type | Description |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `refactor` | Code change that neither fixes nor adds |
| `docs` | Documentation only |
| `test` | Adding or updating tests |
| `perf` | Performance improvement |
| `chore` | Build, CI, tooling changes |

### Examples

```
feat(ordering): implement order grace period cancellation

- Add GracePeriodExpiredDomainEvent
- Update OrderFulfillmentSaga to handle grace period
- Add configuration for grace period duration

Closes #456

---

fix(inventory): prevent race condition in stock reservation

The stock reservation was not using optimistic concurrency,
allowing overselling under high load.

- Add RowVersion to Stock entity
- Update ReserveStock handler to retry on concurrency conflict
- Add integration test for concurrent reservations

---

refactor(payments): extract IPaymentGateway abstraction

Prepare for multiple payment provider support by extracting
a common interface from the Stripe-specific implementation.
```

---

## Testing Requirements

### Minimum Requirements

| Change Type | Required Tests |
|-------------|----------------|
| New aggregate | Unit tests for all methods |
| New command handler | Unit + integration test |
| Bug fix | Regression test |
| Refactor | Existing tests must pass |

### Test Quality

```csharp
// ✅ DO: Test business rules, not implementation
[Fact]
public void Submit_WithEmptyOrder_ShouldFail()
{
    // Arrange
    var order = Order.Create(customerId, address, "key");

    // Act
    var result = order.Submit();

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("Order.Empty");
}

// ❌ DON'T: Test implementation details
[Fact]
public void Submit_ShouldSetStatusTo1()  // Don't expose internal status codes
```

### Coverage Expectations

- Domain logic: 90%+
- Application handlers: 80%+
- New features: Must include tests

---

## Documentation

### When to Update Docs

- New public API endpoints → Update [API_REFERENCE.md](API_REFERENCE.md)
- New patterns or conventions → Update relevant guide
- Configuration changes → Update [DEPLOYMENT.md](DEPLOYMENT.md)
- Security changes → Update [SECURITY.md](SECURITY.md)

### XML Documentation

```csharp
/// <summary>
/// Creates a new order for the specified customer.
/// </summary>
/// <param name="customerId">The unique identifier of the customer.</param>
/// <param name="address">The shipping address for the order.</param>
/// <param name="idempotencyKey">Key to prevent duplicate order creation.</param>
/// <returns>A new Order in Draft status.</returns>
/// <exception cref="ArgumentException">Thrown when address is invalid.</exception>
public static Order Create(Guid customerId, ShippingAddress address, string idempotencyKey)
```

---

## Review Process

### Timeline

- Initial review: Within 2 business days
- Follow-up reviews: Within 1 business day
- Merge after approval: Same day

### Approval Requirements

| Change Size | Approvals Required |
|-------------|-------------------|
| Small (< 100 lines) | 1 |
| Medium (100-500 lines) | 2 |
| Large (> 500 lines) | 2 + architect review |
| Architecture changes | Team lead approval |

### Addressing Feedback

- Respond to all comments
- Push fixes as new commits (easier to review)
- Request re-review when ready
- Squash commits before merge

---

## Questions?

- Create a GitHub Discussion for general questions
- Open an Issue for bugs or feature requests
- Tag `@platform-team` for architecture questions

---

**Thank you for contributing to NetCommerce!** 🚀
