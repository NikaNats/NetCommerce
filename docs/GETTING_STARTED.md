# Getting Started with NetCommerce

> **Complete developer onboarding guide for the NetCommerce platform**

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Start](#quick-start)
3. [Development Environment Setup](#development-environment-setup)
4. [Running the Application](#running-the-application)
5. [Exploring the System](#exploring-the-system)
6. [Your First Feature](#your-first-feature)
7. [Common Development Tasks](#common-development-tasks)
8. [IDE Configuration](#ide-configuration)
9. [FAQ](#faq)

---

## Prerequisites

### Required Software

| Software | Version | Purpose | Installation |
|----------|---------|---------|--------------|
| **.NET SDK** | 10.0 (Preview) | Runtime and build tools | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Docker Desktop** | 4.25+ | Container orchestration | [Download](https://www.docker.com/products/docker-desktop) |
| **Git** | 2.40+ | Version control | [Download](https://git-scm.com/downloads) |

### Recommended Software

| Software | Purpose |
|----------|---------|
| **Visual Studio 2022** (17.12+) | Full IDE with debugging |
| **VS Code** | Lightweight editing |
| **JetBrains Rider** | Cross-platform IDE |
| **Azure Data Studio** | PostgreSQL management |
| **Postman / Bruno** | API testing |

### System Requirements

- **RAM**: 16 GB minimum (32 GB recommended for full stack)
- **Disk**: 20 GB free space
- **OS**: Windows 10/11, macOS 12+, or Linux (Ubuntu 22.04+)

### Verify Prerequisites

```powershell
# Check .NET SDK
dotnet --version
# Expected: 10.0.xxx

# Check Docker
docker --version
# Expected: Docker version 24.x.x or higher

# Verify Docker is running
docker ps
# Should return empty list or running containers (no error)
```

---

## Quick Start

**Get the application running in under 5 minutes:**

```powershell
# 1. Clone the repository
git clone https://github.com/NikaNats/NetCommerce.git
cd NetCommerce

# 2. Trust the development HTTPS certificate
dotnet dev-certs https --trust

# 3. Run the application with Aspire
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj

# 4. Wait for containers to start (first run: ~2-3 minutes)
# Look for: "Application started. Press Ctrl+C to shut down."
```

**Access Points** (check console output for exact ports):

| Service | URL | Credentials |
|---------|-----|-------------|
| **Aspire Dashboard** | `https://localhost:17225` | None |
| **API Swagger** | `https://localhost:{port}/swagger` | See [Test Users](#test-users) |
| **Keycloak Admin** | `http://localhost:8080` | admin / admin |
| **pgAdmin** | `http://localhost:5050` | admin@admin.com / admin |
| **Seq Logging** | `http://localhost:5341` | None |
| **Redis Insight** | `http://localhost:8001` | None |

---

## Development Environment Setup

### 1. Clone and Restore

```powershell
# Clone with full history
git clone https://github.com/NikaNats/NetCommerce.git
cd NetCommerce

# Restore NuGet packages
dotnet restore NetCommerce.slnx

# Build the solution
dotnet build NetCommerce.slnx
```

### 2. Configure User Secrets (Optional)

For local development overrides:

```powershell
# Navigate to API project
cd src/Api

# Initialize user secrets
dotnet user-secrets init

# Set optional overrides
dotnet user-secrets set "Stripe:ApiKey" "sk_test_your_key"
dotnet user-secrets set "AzureBlob:ConnectionString" "your_connection_string"
```

### 3. Docker Configuration

Ensure Docker has sufficient resources:

1. Open Docker Desktop → Settings → Resources
2. Configure:
   - **CPUs**: 4+
   - **Memory**: 8 GB+
   - **Swap**: 2 GB
   - **Disk**: 50 GB

### 4. Environment Variables (Production-like)

Create a `.env` file in the root (gitignored):

```env
# PostgreSQL
POSTGRES_PASSWORD=YourSecurePassword123!

# Meilisearch
MEILISEARCH_MASTER_KEY=your-master-key-min-16-chars

# Stripe (test mode)
STRIPE_API_KEY=sk_test_...
STRIPE_WEBHOOK_SECRET=whsec_...
```

---

## Running the Application

### Option 1: Aspire Orchestration (Recommended)

The Aspire AppHost automatically provisions all infrastructure:

```powershell
# From repository root
dotnet run --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

**What Aspire Starts:**
- PostgreSQL with per-module databases (Catalog, Ordering, Inventory, Payments)
- Redis for caching, baskets, and distributed locking
- Keycloak for identity management
- Seq for structured logging
- Meilisearch for product search
- Azure Storage Emulator (Azurite) for blob storage
- pgAdmin for database management
- Redis Insight for cache inspection

### Option 2: API Only (Advanced)

If you have external infrastructure:

```powershell
# Set connection strings
$env:ConnectionStrings__CatalogDb = "Host=localhost;Database=catalog;..."
$env:ConnectionStrings__redis = "localhost:6379"
# ... other connection strings

# Run API directly
dotnet run --project src/Api/NetCommerce.Api.csproj
```

### Option 3: Watch Mode (Hot Reload)

```powershell
dotnet watch --project src/NetCommerce.AppHost/NetCommerce.AppHost.csproj
```

---

## Exploring the System

### Test Users

Pre-configured users in Keycloak:

| Role | Email | Password | Capabilities |
|------|-------|----------|--------------|
| **Admin** | admin@netcommerce.com | Admin123! | Full system access |
| **Vendor** | vendor@netcommerce.com | Vendor123! | Product & inventory management |
| **Customer** | customer@netcommerce.com | Customer123! | Browsing, cart, checkout |

### Swagger Authentication

1. Open Swagger UI at `https://localhost:{port}/swagger`
2. Click **Authorize** button
3. Select **OAuth2 (Keycloak)**
4. Login with a test user
5. Token is automatically attached to requests

### Aspire Dashboard

The Aspire Dashboard (`https://localhost:17225`) provides:

- **Resources**: View all running services and their status
- **Console**: Live logs from all services
- **Traces**: Distributed tracing across requests
- **Metrics**: Real-time performance metrics

### Database Access

**Via pgAdmin:**
1. Open `http://localhost:5050`
2. Login: admin@admin.com / admin
3. Add server:
   - Host: `postgres` (Docker network)
   - Port: 5432
   - Username: `postgres`
   - Password: (from Aspire console output)

**Via CLI:**
```powershell
# Get container ID
docker ps | grep postgres

# Connect to PostgreSQL
docker exec -it <container-id> psql -U postgres -d catalog
```

### Exploring Logs

**Seq** (`http://localhost:5341`):
- Search: `@Level = 'Error'` for errors
- Search: `RequestPath like '/api/orders%'` for order operations
- Search: `SourceContext = 'Wolverine'` for messaging

---

## Your First Feature

Let's add a simple feature: **Product Rating**.

### Step 1: Define the Domain Model

Create `src/Catalog/Domain/Products/ProductRating.cs`:

```csharp
using NetCommerce.Kernel.Core.Domain;

namespace NetCommerce.Catalog.Domain.Products;

/// <summary>
/// Value object representing a product rating.
/// </summary>
public sealed class ProductRating : ValueObject
{
    public int Value { get; }

    private ProductRating(int value) => Value = value;

    public static Result<ProductRating> Create(int value)
    {
        if (value < 1 || value > 5)
            return Result.Failure<ProductRating>(
                Error.Validation("Rating must be between 1 and 5"));

        return new ProductRating(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

### Step 2: Add to Aggregate

Update `Product.cs` to include ratings:

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    private readonly List<ProductRating> _ratings = [];

    public IReadOnlyList<ProductRating> Ratings => _ratings.AsReadOnly();
    public double AverageRating => _ratings.Count > 0
        ? _ratings.Average(r => r.Value)
        : 0;

    public Result AddRating(int value)
    {
        var ratingResult = ProductRating.Create(value);
        if (ratingResult.IsFailure)
            return ratingResult;

        _ratings.Add(ratingResult.Value);
        RaiseDomainEvent(new ProductRatedDomainEvent(Id, value));
        return Result.Success();
    }
}
```

### Step 3: Create Command Handler

Create `src/Catalog/Application/Products/Commands/RateProductHandler.cs`:

```csharp
using NetCommerce.Kernel.Core.Results;
using Wolverine;

namespace NetCommerce.Catalog.Application.Products.Commands;

public sealed record RateProductCommand(Guid ProductId, int Rating);

[WolverineHandler]
public static class RateProductHandler
{
    public static async Task<Result> Handle(
        RateProductCommand command,
        IProductRepository repository,
        ILogger<RateProductCommand> logger)
    {
        var product = await repository.GetByIdAsync(command.ProductId);
        if (product is null)
            return Result.Failure(Error.NotFound("Product", command.ProductId));

        var result = product.AddRating(command.Rating);
        if (result.IsFailure)
            return result;

        await repository.UpdateAsync(product);

        logger.LogInformation(
            "Product {ProductId} rated {Rating}/5",
            command.ProductId,
            command.Rating);

        return Result.Success();
    }
}
```

### Step 4: Add API Endpoint

Create endpoint in `src/Api/Endpoints/Catalog/ProductRatingEndpoints.cs`:

```csharp
using NetCommerce.Catalog.Application.Products.Commands;
using Wolverine;

namespace NetCommerce.Api.Endpoints.Catalog;

public static class ProductRatingEndpoints
{
    public static void MapProductRatingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/products/{productId:guid}/ratings")
            .WithTags("Products");

        group.MapPost("/", async (
            Guid productId,
            RateProductRequest request,
            IMessageBus bus) =>
        {
            var result = await bus.InvokeAsync<Result>(
                new RateProductCommand(productId, request.Rating));

            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(result.Error.Description, statusCode: result.Error.StatusCode);
        })
        .RequireAuthorization("CustomerAccess")
        .WithName("RateProduct");
    }
}

public sealed record RateProductRequest(int Rating);
```

### Step 5: Run Tests

```powershell
# Run all tests
dotnet test NetCommerce.slnx

# Run specific test project
dotnet test tests/NetCommerce.Domain.Tests

# Run architecture tests (validates Clean Architecture)
dotnet test tests/NetCommerce.Architecture.Tests
```

---

## Common Development Tasks

### Adding a New Module

1. **Create project structure:**
```
src/NewModule/
├── NewModule.Domain/
├── NewModule.Application/
└── NewModule.Infrastructure/
```

2. **Add to solution:**
```powershell
dotnet new classlib -n NewModule.Domain -o src/NewModule/NewModule.Domain
dotnet sln add src/NewModule/NewModule.Domain/NewModule.Domain.csproj
# Repeat for Application and Infrastructure
```

3. **Add project references** following Clean Architecture:
   - Domain: References `NetCommerce.Kernel.Core`
   - Application: References Domain, `NetCommerce.Kernel.Application`
   - Infrastructure: References Application, `NetCommerce.Kernel.EfCore`

4. **Create DbContext** with module schema:
```csharp
public class NewModuleDbContext : BaseDbContext
{
    public const string Schema = "newmodule";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }
}
```

5. **Register in AppHost:**
```csharp
var newModuleDb = postgres.AddDatabase("NewModuleDb", "newmodule");
api.WithReference(newModuleDb);
```

### Adding a Migration

```powershell
# Navigate to Infrastructure project
cd src/Ordering/Ordering.Infrastructure

# Add migration
dotnet ef migrations add AddProductRatings \
    --startup-project ../../Api/NetCommerce.Api.csproj \
    --context OrderingDbContext

# Apply migration (happens automatically on startup)
```

### Running Load Tests

```powershell
# Run NBomber load tests
dotnet test tests/NetCommerce.LoadTests \
    --filter "Category=LoadTest" \
    --logger "console;verbosity=detailed"
```

### Debugging Wolverine Messages

1. Check Seq for message logs
2. Query outbox/inbox tables:
```sql
-- Pending outbox messages
SELECT * FROM wolverine.wolverine_outgoing_envelopes;

-- Failed messages (DLQ)
SELECT * FROM wolverine.wolverine_incoming_envelopes
WHERE status = 'dead_letter';
```

---

## IDE Configuration

### Visual Studio 2022

1. Open `NetCommerce.slnx`
2. Set startup project: `NetCommerce.AppHost`
3. Debug configuration: Use **Aspire** profile

**Recommended Extensions:**
- GitHub Copilot
- CodeMaid
- Productivity Power Tools

### VS Code

**Required Extensions:**
- C# Dev Kit
- .NET Aspire
- Docker
- PostgreSQL

**Launch Configuration** (`.vscode/launch.json`):
```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Aspire AppHost",
            "type": "dotnet",
            "request": "launch",
            "projectPath": "${workspaceFolder}/src/NetCommerce.AppHost/NetCommerce.AppHost.csproj"
        }
    ]
}
```

### JetBrains Rider

1. Open solution file
2. Configure **Run Configuration**:
   - Type: .NET Project
   - Project: NetCommerce.AppHost
   - Framework: net10.0

---

## FAQ

### Q: First run is slow. Why?

**A:** Docker pulls images for PostgreSQL, Redis, Keycloak, etc. Subsequent runs use cached images.

### Q: How do I reset the database?

```powershell
# Stop application
# Then remove volumes:
docker volume rm netcommerce_postgres-data
docker volume rm netcommerce_redis-data

# Restart application - databases will be recreated
```

### Q: How do I see what Wolverine is doing?

**A:** Check Seq logs filtered by `SourceContext = 'Wolverine'` or enable verbose logging:
```json
{
  "Logging": {
    "LogLevel": {
      "Wolverine": "Debug"
    }
  }
}
```

### Q: Tests are failing with database errors

**A:** Ensure Docker is running and Testcontainers can create containers:
```powershell
docker run --rm -it postgres:16 psql --version
```

### Q: How do I add a new integration event?

1. Define event in `src/Domain.Shared/Events/`:
```csharp
public sealed record MyNewIntegrationEvent(
    Guid CorrelationId,
    string Data) : IntegrationEvent;
```

2. Create handler in consumer module:
```csharp
[WolverineHandler]
public static class MyNewEventHandler
{
    public static void Handle(MyNewIntegrationEvent @event, ILogger logger)
    {
        logger.LogInformation("Handling event: {Data}", @event.Data);
    }
}
```

### Q: Where are the API routes defined?

**A:** `src/Api/Endpoints/{Module}/` - Each module has its endpoint group.

### Q: How do I contribute?

**A:** See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## Next Steps

1. **Read the Architecture Guide**: [ARCHITECTURE.md](ARCHITECTURE.md)
2. **Understand Domain Model**: [DOMAIN_MODEL.md](DOMAIN_MODEL.md)
3. **Learn Messaging Patterns**: [MESSAGING_PATTERNS.md](MESSAGING_PATTERNS.md)
4. **Review Security**: [SECURITY.md](SECURITY.md)

---

**Need Help?**
- Create a GitHub Issue
- Check existing documentation in `/docs`
- Review test files for usage examples

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Maintainer:** NetCommerce Team
