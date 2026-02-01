# NetCommerce Security Architecture

> **Zero-Trust Identity Mesh, RBAC, and Data Protection**

---

## Table of Contents

1. [Security Philosophy](#security-philosophy)
2. [Identity Mesh Architecture](#identity-mesh-architecture)
3. [Keycloak Configuration](#keycloak-configuration)
4. [JWT Authentication Flow](#jwt-authentication-flow)
5. [Token Introspection (Kill Switch)](#token-introspection-kill-switch)
6. [Token Exchange (RFC 8693)](#token-exchange-rfc-8693)
7. [Role-Based Access Control (RBAC)](#role-based-access-control-rbac)
8. [API Authorization Policies](#api-authorization-policies)
9. [Multi-Tenancy Security](#multi-tenancy-security)
10. [Data Protection & PII](#data-protection--pii)
11. [Idempotency & Replay Protection](#idempotency--replay-protection)
12. [Rate Limiting](#rate-limiting)
13. [Security Monitoring](#security-monitoring)
14. [Threat Model](#threat-model)

---

## Security Philosophy

NetCommerce implements **Zero-Trust Security** principles:

| Principle | Implementation |
|-----------|----------------|
| Never trust, always verify | Token introspection on every request |
| Assume breach | Short-lived tokens (15 min), instant revocation |
| Least privilege | Token exchange for downstream calls |
| Defense in depth | Multiple security layers |
| Continuous verification | Real-time token validation |

### 2025 Security Standards

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ZERO-TRUST SECURITY MODEL                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  TRADITIONAL MODEL (Deprecated)                                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  Trust boundary at perimeter                                      │      │
│  │  Once inside, tokens trusted until expiration                    │      │
│  │  15-minute window for banned users to access system              │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
│  ZERO-TRUST MODEL (NetCommerce)                                             │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  No implicit trust, even inside network                          │      │
│  │  Token validated against IdP on EVERY request                    │      │
│  │  Banned users blocked within 30 seconds (cache TTL)              │      │
│  │  Token exchange for downstream services (least privilege)        │      │
│  │  Continuous authorization, not point-in-time                     │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Identity Mesh Architecture

### Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    IDENTITY MESH ARCHITECTURE                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│    ┌─────────────────────────────────────────────────────────┐             │
│    │                    KEYCLOAK                              │             │
│    │              (Identity Provider)                         │             │
│    │                                                          │             │
│    │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │             │
│    │  │    Users     │  │    Roles     │  │   Clients    │  │             │
│    │  │  - admin     │  │  - admin     │  │  - api       │  │             │
│    │  │  - customer  │  │  - vendor    │  │  - frontend  │  │             │
│    │  │  - vendor    │  │  - customer  │  │  - mobile    │  │             │
│    │  └──────────────┘  └──────────────┘  └──────────────┘  │             │
│    │                                                          │             │
│    │  ┌──────────────┐  ┌──────────────┐                    │             │
│    │  │ Introspection│  │   Token      │                    │             │
│    │  │   Endpoint   │  │  Exchange    │                    │             │
│    │  │ (RFC 7662)   │  │  (RFC 8693)  │                    │             │
│    │  └──────────────┘  └──────────────┘                    │             │
│    └─────────────────────────────────────────────────────────┘             │
│                              │                                              │
│              ┌───────────────┼───────────────┐                             │
│              │               │               │                             │
│              ▼               ▼               ▼                             │
│    ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                   │
│    │   API        │  │  Frontend    │  │   Mobile     │                   │
│    │   Service    │  │   SPA        │  │    App       │                   │
│    │              │  │              │  │              │                   │
│    │ • JWT Valid  │  │ • PKCE Auth  │  │ • PKCE Auth  │                   │
│    │ • Introspect │  │              │  │              │                   │
│    │ • Exchange   │  │              │  │              │                   │
│    └──────────────┘  └──────────────┘  └──────────────┘                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Configuration

```csharp
// src/Api/Program.cs
builder.AddZeroTrustAuthentication(options =>
{
    // Token introspection (kill switch) - validates against IdP
    options.IntrospectionEnabled = true;
    options.IntrospectionCacheSeconds = 30;  // Max 30s for instant bans

    // Token exchange for downstream service calls
    options.TokenExchangeEnabled = true;
});

// In request pipeline
app.UseAuthentication();
app.UseAuthorization();
app.UseZeroTrustMiddleware();  // Introspection happens here
```

---

## Keycloak Configuration

### Realm Setup

Keycloak is configured via Aspire with the following realm structure:

```
netcommerce (Realm)
├── Clients
│   ├── netcommerce-api (confidential)
│   │   ├── Direct Access Grants: disabled
│   │   ├── Service Accounts: enabled
│   │   ├── Mappers: audience, roles
│   │   └── Token Exchange: allowed
│   ├── netcommerce-frontend (public)
│   │   ├── PKCE: S256 required
│   │   └── Valid Redirect URIs: http://localhost:*
│   └── netcommerce-mobile (public)
│       └── PKCE: S256 required
├── Roles
│   ├── admin
│   ├── vendor
│   └── customer
├── Groups
│   ├── Administrators → admin role
│   ├── Vendors → vendor role
│   └── Customers → customer role
└── Identity Providers (optional)
    ├── Google
    └── Microsoft
```

### Environment Variables (Aspire-Injected)

```bash
# Injected by Aspire orchestration
Keycloak__AuthServerUrl=http://localhost:8080
Keycloak__Realm=netcommerce

# Override in configuration
Auth__Audience=netcommerce-api
Auth__ApiScope=netcommerce.api
Auth__ClientId=netcommerce-api
Auth__ClientSecret=<secret>
Auth__IntrospectionEnabled=true
Auth__IntrospectionCacheSeconds=30
Auth__TokenExchangeEnabled=true
```

### Keycloak Token Example

```json
{
  "exp": 1735689600,
  "iat": 1735688700,
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "typ": "Bearer",
  "azp": "netcommerce-frontend",
  "aud": ["netcommerce-api"],
  "realm_access": {
    "roles": ["customer"]
  },
  "resource_access": {
    "netcommerce-api": {
      "roles": ["order:read", "order:write"]
    }
  },
  "scope": "openid profile email netcommerce.api",
  "tenant_id": "550e8400-e29b-41d4-a716-446655440001"
}
```

---

## JWT Authentication Flow

### Sequence Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    JWT AUTHENTICATION FLOW                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Client                    API                       Keycloak               │
│    │                        │                           │                   │
│    │  1. GET /api/orders    │                           │                   │
│    │  Authorization: Bearer │                           │                   │
│    │ ─────────────────────▶ │                           │                   │
│    │                        │                           │                   │
│    │                        │  2. Validate JWT          │                   │
│    │                        │     (signature, exp, aud) │                   │
│    │                        │                           │                   │
│    │                        │  3. POST /introspect      │                   │
│    │                        │     (if enabled)          │                   │
│    │                        │ ────────────────────────▶ │                   │
│    │                        │                           │                   │
│    │                        │  4. { "active": true }    │                   │
│    │                        │ ◀──────────────────────── │                   │
│    │                        │                           │                   │
│    │                        │  5. Cache result (30s)    │                   │
│    │                        │                           │                   │
│    │                        │  6. Transform claims      │                   │
│    │                        │     (flatten roles)       │                   │
│    │                        │                           │                   │
│    │                        │  7. Check authorization   │                   │
│    │                        │     policy                │                   │
│    │                        │                           │                   │
│    │  8. 200 OK             │                           │                   │
│    │ ◀───────────────────── │                           │                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### JWT Bearer Configuration

```csharp
/// <summary>
/// Configures JWT Bearer authentication for Zero-Trust.
/// </summary>
internal sealed class ZeroTrustJwtBearerOptionsSetup : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        // 1. Authority URL (Keycloak realm endpoint)
        options.Authority = _authOptions.RealmUrl;

        // 2. Audience validation
        options.Audience = _authOptions.Audience;

        // 3. HTTPS metadata requirement (relaxed for dev)
        options.RequireHttpsMetadata = !_environment.IsDevelopment();

        // 4. Token validation parameters
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)  // Reduced from default 5 minutes
        };

        // 5. Error handling
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                _logger.LogWarning("Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    }
}
```

---

## Token Introspection (Kill Switch)

### The Problem Token Introspection Solves

```
SCENARIO: User is banned/compromised at 10:00:00

WITHOUT INTROSPECTION:
├── 10:00:00 - User banned in Keycloak
├── 10:00:01 - User makes request with valid JWT
├── 10:00:02 - API validates JWT signature → PASSES
├── 10:00:03 - User accesses sensitive data ⚠️
├── ...
├── 10:14:59 - JWT still valid (15 min expiry)
└── 10:15:00 - JWT finally expires, user blocked

WITH INTROSPECTION:
├── 10:00:00 - User banned in Keycloak
├── 10:00:01 - User makes request with valid JWT
├── 10:00:02 - API validates JWT signature → PASSES
├── 10:00:03 - API introspects token → "active": false
└── 10:00:03 - User immediately blocked ✓
```

### Implementation

```csharp
/// <summary>
/// Token Introspection Middleware (The "Kill Switch")
/// Validates token against IdP on every request.
/// </summary>
public sealed class TokenIntrospectionMiddleware
{
    public async Task InvokeAsync(HttpContext context, ...)
    {
        // 1. Skip if introspection disabled
        if (!authOptions.IntrospectionEnabled)
        {
            await _next(context);
            return;
        }

        // 2. Get access token
        var token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);  // Let standard AuthN handle 401
            return;
        }

        // 3. Check cache first (performance)
        var cacheKey = $"introspection:{ComputeTokenHash(token)}";
        var cachedResult = await cache.GetStringAsync(cacheKey);

        if (cachedResult == "revoked")
        {
            await RejectRequest(context, "Token has been revoked");
            return;
        }

        if (cachedResult == "active")
        {
            await _next(context);
            return;
        }

        // 4. Introspect against IdP (RFC 7662)
        var result = await IntrospectTokenAsync(token, authOptions, clientFactory);

        // 5. Cache result
        await cache.SetStringAsync(cacheKey,
            result.IsActive ? "active" : "revoked",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });

        // 6. Reject if not active
        if (!result.IsActive)
        {
            await RejectRequest(context, "Token has been revoked");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// RFC 7662 Token Introspection request.
    /// </summary>
    private async Task<IntrospectionResult> IntrospectTokenAsync(
        string token,
        ZeroTrustAuthOptions options,
        IHttpClientFactory clientFactory)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret
        });

        var response = await client.PostAsync(options.IntrospectionEndpoint, content);
        var json = await response.Content.ReadAsStringAsync();

        // { "active": false } means token is revoked
        using var doc = JsonDocument.Parse(json);
        var active = doc.RootElement.GetProperty("active").GetBoolean();

        return new IntrospectionResult(active, active ? null : "Token inactive");
    }
}
```

### Introspection Performance

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    INTROSPECTION PERFORMANCE                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  First request (cache miss):                                                │
│  ├── Introspect against Keycloak: ~10-50ms                                 │
│  ├── Cache result in Redis: ~1ms                                           │
│  └── Total overhead: ~15-55ms                                              │
│                                                                             │
│  Subsequent requests (cache hit):                                           │
│  ├── Redis lookup: ~1ms                                                    │
│  └── Total overhead: ~1-2ms                                                │
│                                                                             │
│  Cache Configuration:                                                       │
│  ├── TTL: 30 seconds (security requirement)                                │
│  ├── Key: SHA256(token)[0:16] (never store actual token)                  │
│  └── Value: "active" | "revoked"                                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Token Exchange (RFC 8693)

### Why Token Exchange?

```
PROBLEM: API needs to call downstream service (e.g., Payments)

WRONG APPROACH (Token Forwarding):
├── User token intended for API
├── API forwards same token to Payments
├── Token has API audience, not Payments
├── If token is leaked at Payments, attacker can access API ⚠️
└── Violates principle of least privilege

CORRECT APPROACH (Token Exchange):
├── User token intended for API
├── API exchanges token for Payments-scoped token
├── New token has Payments audience only
├── If token is leaked at Payments, cannot access API ✓
└── Least privilege maintained
```

### Implementation

```csharp
/// <summary>
/// RFC 8693 Token Exchange Delegating Handler.
/// Exchanges user token for service-specific token.
/// </summary>
public sealed class TokenExchangeDelegatingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 1. Get incoming user token
        var incomingToken = await httpContext.GetTokenAsync("access_token");

        // 2. Check cache for exchanged token
        var cacheKey = $"token_exchange:{ComputeTokenHash(incomingToken)}:{_targetAudience}";
        var exchangedToken = await _cache.GetStringAsync(cacheKey);

        // 3. Exchange if not cached
        if (string.IsNullOrEmpty(exchangedToken))
        {
            var result = await ExchangeTokenAsync(incomingToken, _targetAudience);
            exchangedToken = result.AccessToken;

            // Cache exchanged token (slightly less than token lifetime)
            await _cache.SetStringAsync(cacheKey, exchangedToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(result.ExpiresIn - 30)
                });
        }

        // 4. Attach exchanged token to downstream request
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", exchangedToken);

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// RFC 8693 Token Exchange request.
    /// </summary>
    private async Task<TokenExchangeResult> ExchangeTokenAsync(
        string subjectToken,
        string targetAudience)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["audience"] = targetAudience,
            ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token"
        });

        var response = await client.PostAsync(options.TokenEndpoint, content);
        // Parse response...
    }
}
```

### Usage

```csharp
// Register HttpClient with token exchange for Payments service
builder.Services.AddHttpClient("PaymentsService", client =>
{
    client.BaseAddress = new Uri("https://payments.internal/");
})
.AddTokenExchange("payments-service");

// Usage in handler
public class ProcessOrderHandler
{
    public async Task Handle(ProcessOrderCommand command, IHttpClientFactory clientFactory)
    {
        var client = clientFactory.CreateClient("PaymentsService");

        // Token automatically exchanged with payments-service audience
        await client.PostAsJsonAsync("/api/charge", new { ... });
    }
}
```

---

## Role-Based Access Control (RBAC)

### Role Hierarchy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ROLE HIERARCHY                                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  admin ──────────────────────────────────────────────────────┐             │
│    │                                                          │             │
│    ├── Full system access                                     │             │
│    ├── User management                                        │             │
│    ├── Order management (all orders)                         │  Inherits   │
│    ├── Product management                                     │             │
│    └── Inventory management                                   │             │
│                                                               │             │
│  vendor ───────────────────────────────────────────┐         │             │
│    │                                                │         │             │
│    ├── Own products only                           │         │             │
│    ├── Own orders only                             │         │             │
│    └── Own inventory only                          │         │             │
│                                                    │         │             │
│  customer ──────────────────────────────────────┐  │         │             │
│    │                                             │  │         │             │
│    ├── Browse catalog (public)                  │  │         │             │
│    ├── Own orders only                          │  │         │             │
│    └── Own profile                              │  │         │             │
│                                                               │             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Claims Transformation

Keycloak returns nested role claims that need flattening:

```csharp
/// <summary>
/// Transforms Keycloak nested role claims into flat .NET claims.
/// </summary>
public sealed class OidcRoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        // 1. Extract realm roles
        // Input: { "realm_access": { "roles": ["admin", "customer"] } }
        // Output: Claim("roles", "admin"), Claim("roles", "customer")
        ExtractRealmRoles(principal, identity);

        // 2. Extract client-specific roles
        // Input: { "resource_access": { "netcommerce-api": { "roles": ["order:read"] } } }
        // Output: Claim("permissions", "order:read")
        ExtractClientRoles(principal, identity);

        return Task.FromResult(principal);
    }

    private static void ExtractRealmRoles(ClaimsPrincipal principal, ClaimsIdentity identity)
    {
        var realmAccess = principal.FindFirst("realm_access");
        if (realmAccess is null) return;

        using var doc = JsonDocument.Parse(realmAccess.Value);
        if (doc.RootElement.TryGetProperty("roles", out var roles))
        {
            foreach (var role in roles.EnumerateArray())
            {
                identity.AddClaim(new Claim("roles", role.GetString()!));
                identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
            }
        }
    }
}
```

---

## API Authorization Policies

### Policy Definitions

```csharp
// src/Api/Program.cs
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole("admin"))
    .AddPolicy("VendorOnly", policy => policy.RequireRole("admin", "vendor"))
    .AddPolicy("CustomerOnly", policy => policy.RequireRole("customer"));
```

### Endpoint Protection

```csharp
// Public endpoint (catalog browsing)
app.MapGet("/api/v1/products", async (IMessageBus bus) =>
{
    var result = await bus.InvokeAsync<Result<List<ProductDto>>>(
        new GetProductsQuery());
    return result.ToActionResult();
});

// Authenticated endpoint
app.MapGet("/api/v1/orders", async (IMessageBus bus, ClaimsPrincipal user) =>
{
    var customerId = user.GetUserId();
    var result = await bus.InvokeAsync<Result<List<OrderDto>>>(
        new GetCustomerOrdersQuery(customerId));
    return result.ToActionResult();
})
.RequireAuthorization();

// Admin-only endpoint
app.MapGet("/api/v1/admin/orders", async (IMessageBus bus) =>
{
    var result = await bus.InvokeAsync<Result<List<OrderDto>>>(
        new GetAllOrdersQuery());
    return result.ToActionResult();
})
.RequireAuthorization("AdminOnly");

// Vendor endpoint
app.MapPost("/api/v1/products", async (CreateProductRequest request, IMessageBus bus) =>
{
    // ...
})
.RequireAuthorization("VendorOnly");
```

### Resource-Level Authorization

```csharp
[WolverineHandler]
public static class GetOrderHandler
{
    public static async Task<Result<OrderDto>> Handle(
        GetOrderQuery query,
        IOrderRepository repository,
        IUserContext userContext)
    {
        var order = await repository.GetByIdAsync(query.OrderId);

        if (order is null)
            return Result.Failure<OrderDto>(Error.NotFound("Order", query.OrderId));

        // Resource-level authorization
        if (!userContext.IsInRole("admin") && order.CustomerId != userContext.UserId)
            return Result.Failure<OrderDto>(Error.Forbidden("Cannot access this order"));

        return Result.Success(order.ToDto());
    }
}
```

---

## Multi-Tenancy Security

### Tenant Isolation

```csharp
/// <summary>
/// HTTP Tenant Context - extracts tenant from JWT claims.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    public Guid? CurrentTenantId { get; }

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = user.FindFirst("tenant_id")?.Value;
            CurrentTenantId = Guid.TryParse(tenantClaim, out var tid) ? tid : null;
        }
    }
}
```

### EF Core Query Filters

```csharp
/// <summary>
/// BaseDbContext applies tenant filter to all multi-tenant entities.
/// </summary>
public abstract class BaseDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply global query filter for multi-tenancy
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                // All queries automatically filtered by tenant
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(CreateTenantFilter(entityType.ClrType));
            }
        }
    }

    private LambdaExpression CreateTenantFilter(Type entityType)
    {
        // WHERE TenantId = @CurrentTenantId OR @CurrentTenantId IS NULL
        var parameter = Expression.Parameter(entityType, "e");
        var tenantIdProperty = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var currentTenant = Expression.Property(
            Expression.Constant(_tenantContext),
            nameof(ITenantContext.CurrentTenantId));

        var filter = Expression.OrElse(
            Expression.Equal(tenantIdProperty, currentTenant),
            Expression.Equal(currentTenant, Expression.Constant(null, typeof(Guid?))));

        return Expression.Lambda(filter, parameter);
    }
}
```

### Cross-Tenant Protection Test

```csharp
[Fact]
public async Task Customer_CannotAccessOtherTenantOrders()
{
    // Arrange: Customer from Tenant A
    var tenantACustomer = await CreateCustomerInTenant(TenantA);
    var tenantAOrder = await CreateOrderForCustomer(tenantACustomer);

    // Arrange: Customer from Tenant B
    var tenantBCustomer = await CreateCustomerInTenant(TenantB);

    // Act: Tenant B customer tries to access Tenant A order
    var client = _factory.CreateClientAs(tenantBCustomer);
    var response = await client.GetAsync($"/api/v1/orders/{tenantAOrder.Id}");

    // Assert: Should be 403 Forbidden or 404 Not Found
    response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
}
```

---

## Data Protection & PII

### PII Handling Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PII DATA PROTECTION                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PII Categories:                                                            │
│  ├── SENSITIVE: SSN, Payment Card Numbers, Passwords                       │
│  │   └── Never stored, tokenized via external service                      │
│  │                                                                          │
│  ├── PERSONAL: Name, Email, Phone, Address                                 │
│  │   └── Encrypted at rest, scrubbed in logs                              │
│  │                                                                          │
│  └── AUDIT: User IDs, Order IDs, Timestamps                                │
│      └── Retained for compliance, no special handling                      │
│                                                                             │
│  Log Scrubbing:                                                             │
│  ├── Email: user@domain.com → u***@d***.com                               │
│  ├── Phone: +1-555-123-4567 → +1-555-***-****                            │
│  ├── Card: 4111-1111-1111-1111 → ****-****-****-1111                     │
│  └── IP: 192.168.1.100 → 192.168.x.x                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Structured Logging with PII Scrubbing

```csharp
// CORRECT: Only log safe identifiers
logger.LogInformation(
    "Processing order {OrderId} for customer {CustomerId}",
    order.Id,
    order.CustomerId);

// INCORRECT: Never log PII directly
// logger.LogInformation("Order for {Email}: {Address}", customer.Email, address);

// Use PII-safe DTOs for logging
public record SafeOrderLogDto(
    Guid OrderId,
    Guid CustomerId,
    string OrderNumber,
    decimal Amount,
    string Status);
```

---

## Idempotency & Replay Protection

### Idempotency Keys

```csharp
// Require idempotency key for state-changing operations
app.MapPost("/api/v1/orders", async (
    CreateOrderRequest request,
    [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
    IMessageBus bus) =>
{
    if (string.IsNullOrEmpty(idempotencyKey))
        return Results.BadRequest(new { error = "X-Idempotency-Key header required" });

    var command = new CreateOrderCommand(
        request.CustomerId,
        request.ShippingAddress,
        request.Items,
        idempotencyKey);

    var result = await bus.InvokeAsync<Result<Guid>>(command);
    return result.ToActionResult();
});
```

### Idempotency Service

```csharp
public interface IIdempotencyService
{
    Task<T?> GetAsync<T>(string key) where T : struct;
    Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : struct;
}

[WolverineHandler]
public static class CreateOrderHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateOrderCommand command,
        IIdempotencyService idempotency,
        IOrderRepository repository)
    {
        // Check if already processed
        var existingOrderId = await idempotency.GetAsync<Guid>(command.IdempotencyKey);
        if (existingOrderId.HasValue)
        {
            return Result.Success(existingOrderId.Value);  // Return same result
        }

        // Process order...
        var order = Order.Create(...);
        await repository.AddAsync(order);

        // Store result for idempotency (24h retention)
        await idempotency.SetAsync(command.IdempotencyKey, order.Id, TimeSpan.FromHours(24));

        return Result.Success(order.Id);
    }
}
```

---

## Rate Limiting

### Configuration

```csharp
builder.Services.AddRateLimiter(options =>
{
    // Global rate limit
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.GetUserId()?.ToString() ?? context.Connection.RemoteIpAddress?.ToString();

        return RateLimitPartition.GetFixedWindowLimiter(userId ?? "anonymous", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 10
            });
    });

    // Stricter limit for authentication endpoints
    options.AddFixedWindowLimiter("auth", options =>
    {
        options.PermitLimit = 10;
        options.Window = TimeSpan.FromMinutes(5);
    });

    // Response customization
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "TOO_MANY_REQUESTS",
            message = "Rate limit exceeded. Please try again later.",
            retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? (int)retryAfter.TotalSeconds
                : 60
        }, token);
    };
});
```

---

## Security Monitoring

### Audit Logging

```csharp
/// <summary>
/// Wolverine middleware for audit logging.
/// </summary>
public sealed class AuditMiddleware
{
    public async Task HandleAsync(IAuditableCommand command, Envelope envelope, ILogger logger)
    {
        logger.LogInformation(
            "Audit: {CommandName} by {UserId} at {Timestamp}. CorrelationId: {CorrelationId}",
            command.CommandName,
            command.UserId,
            DateTime.UtcNow,
            envelope.CorrelationId);
    }
}
```

### Seq Queries for Security Events

```
// Failed authentication attempts
@Level = 'Warning' and Message like 'Authentication failed%'

// Token introspection failures (potential attack)
@Level = 'Warning' and Message like 'Token introspection failed%'

// Unauthorized access attempts
StatusCode = 403

// Rate limit hits
StatusCode = 429

// Suspicious patterns
UserId = 'xxx' and @Timestamp >= Now() - 1h | count()
```

### Security Metrics

```csharp
public class SecurityMetrics
{
    private readonly Counter<long> _authFailures;
    private readonly Counter<long> _rateLimitHits;
    private readonly Counter<long> _tokenRevocations;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("NetCommerce.Security");

        _authFailures = meter.CreateCounter<long>("auth.failures");
        _rateLimitHits = meter.CreateCounter<long>("ratelimit.hits");
        _tokenRevocations = meter.CreateCounter<long>("token.revocations");
    }
}
```

---

## Threat Model

### STRIDE Analysis

| Threat | Mitigation |
|--------|------------|
| **S**poofing | JWT validation, token introspection |
| **T**ampering | Digital signatures, HTTPS |
| **R**epudiation | Audit logging, correlation IDs |
| **I**nformation Disclosure | PII scrubbing, encryption at rest |
| **D**enial of Service | Rate limiting, request size limits |
| **E**levation of Privilege | RBAC, tenant isolation, least privilege |

### Security Checklist

- [ ] Token introspection enabled in production
- [ ] Introspection cache TTL ≤ 30 seconds
- [ ] Token exchange for downstream services
- [ ] Rate limiting configured
- [ ] HTTPS enforced in production
- [ ] PII scrubbed from logs
- [ ] Audit logging enabled
- [ ] Cross-tenant data access tested
- [ ] Idempotency keys required for mutations
- [ ] Client secrets in secure storage (Key Vault)

---

**Document Version:** 1.0
**Last Updated:** February 2026
**Security Review:** Quarterly
**Maintainer:** NetCommerce Security Team
