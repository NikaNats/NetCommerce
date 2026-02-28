# Security

Security architecture, authentication, authorization, data protection, and hardening measures in NetCommerce.

## Authentication

### Zero-Trust Identity Mesh

NetCommerce implements a Zero-Trust authentication model via the `ZeroTrustAuthenticationExtensions` in `NetCommerce.Kernel.Security`. Every request is validated — no implicit trust based on network location.

**Setup in `Program.cs`:**
```csharp
builder.AddZeroTrustAuthentication();
// ...
app.UseZeroTrustMiddleware(); // After UseAuthentication/UseAuthorization
```

### Identity Provider (Keycloak)

Keycloak serves as the external identity provider via OpenID Connect:

| Setting | Source | Description |
|---|---|---|
| `Keycloak:AuthServerUrl` | Aspire | Keycloak server URL |
| `Keycloak:Realm` | Aspire | Keycloak realm name |
| `Auth:Audience` | Config | JWT audience claim |
| `Auth:ApiScope` | Config | Required API scope |
| `Auth:ClientId` | Config | OAuth client ID |
| `Auth:ClientSecret` | Config | OAuth client secret |

### JWT Validation Rules

```
Issuer:          Validated — must match Keycloak realm URL
Audience:        Validated — must match configured audience
Lifetime:        Validated — token must not be expired
Clock skew:      30 seconds maximum (tight for security)
HTTPS metadata:  Required in production, relaxed in development
MapInboundClaims: Disabled — preserves Keycloak's original claim names
```

Claim mapping:
- `NameClaimType` → `preferred_username`
- `RoleClaimType` → `roles`

### Token Introspection (Kill Switch)

Optional token introspection enables instant token revocation without waiting for JWT expiry:

```
Auth:IntrospectionEnabled   = true      # Enable introspection
Auth:IntrospectionCacheSeconds = 60     # Cache introspection results
```

The `TokenIntrospectionMiddleware` calls Keycloak's introspection endpoint to verify tokens are still active. Results are cached to amortize latency.

### Token Exchange

For downstream service calls, `TokenExchangeHandlerFactory` performs RFC 8693 token exchange:

```csharp
builder.Services.AddHttpClient("InventoryService")
    .AddTokenExchange("inventory-service");
```

This exchanges the user's token for a narrowly-scoped token targeted at the downstream service.

### BFF Token Proxy

`KeycloakTokenProxy` implements the Backend-for-Frontend pattern. All token lifecycle management (login, refresh, logout) is delegated to the server — the frontend never handles tokens directly. Resource Owner Password Credentials (ROPC) is rejected.

Authentication endpoints:
- `POST /api/v1/auth/login` — BFF login (exchanges credentials via Keycloak)
- `POST /api/v1/auth/register` — User registration via Keycloak Admin API
- `POST /api/v1/auth/refresh` — Token refresh (server-side)
- `POST /api/v1/auth/logout` — Server-side logout (revokes tokens)
- `GET /api/v1/auth/me` — Current user profile

All auth endpoints are rate-limited with the `AuthStrict` policy (5 requests/minute per IP).

## Authorization

### Role-Based Access Control (RBAC)

Five authorization policies are configured:

| Policy | Requirement | Usage |
|---|---|---|
| `AdminOnly` | `admin` role | Admin-only management |
| `VendorOnly` | `admin` or `vendor` role | Product/category/inventory management |
| `CustomerOnly` | `customer` role | Customer-specific operations |
| `OwnerOnly` | Resource owner match | Users can only access their own resources |
| `AdminElevated` | `admin` role + X-Admin-Api-Key header + IP allowlist | Destructive admin operations (DLQ, finance, saga recovery) |

### Claims Transformation

`OidcRoleClaimsTransformation` flattens nested Keycloak role claims (from `realm_access.roles` and `resource_access.{client}.roles`) into flat .NET `roles` claims, enabling standard `policy.RequireRole()` checks.

### Admin Elevated Authorization

The `AdminElevated` policy implements defense-in-depth for critical operations:

1. **Role check** — must have `admin` role
2. **API key** — must provide valid `X-Admin-Api-Key` header
3. **IP allowlist** — request must originate from allowed IP range (configurable)

Configuration:
```json
{
  "AdminApiKey": {
    "Key": "your-secret-api-key",
    "AllowedIpRanges": ["10.0.0.0/8", "172.16.0.0/12"]
  }
}
```

### Resource Owner Authorization

`ResourceOwnerAuthorizationHandler` ensures users can only access their own resources. The handler extracts the `sub` claim and compares it against the route parameter (default: `userId`).

```csharp
// Endpoint registration
group.MapGet("/{userId}/orders", ...)
    .RequireAuthorization("OwnerOnly");
```

## Data Protection

### PII Vault Architecture

Personally Identifiable Information is encrypted at rest using a two-tier key hierarchy:

```
Cloud Master Key (KMS/HSM)
    └── Data Encryption Key (DEK) — wrapped/unwrapped via IKeyManagementService
            └── AES-256-GCM encryption of PII fields
```

#### Encryption Flow

1. `IKeyManagementService.GetActiveEncryptedDekAsync()` retrieves the wrapped DEK
2. `IKeyManagementService.UnwrapKeyAsync()` decrypts the DEK using the cloud master key
3. `ICryptoProvider.Encrypt()` encrypts plaintext using AES-256-GCM with the DEK
4. `EncryptedData` is stored in its self-describing format:

```
v1|aAES-256-GCM:1|k{keyId}|{iv}|{ciphertext}|{encryptedDek}
```

#### Blind Indexes

For searchable encrypted fields, `BlindIndex` computes HMAC-SHA256 hashes:

```csharp
BlindIndex.Compute("user@example.com", salt);
// Produces: deterministic hash for searching without decryption
```

**Salt management** via `IBlindIndexSaltProvider`:
- `GetCurrentSaltAsync()` — active salt for new indexes
- `GetSaltByVersionAsync(version)` — historical salts for searching
- `GetCurrentSaltVersionAsync()` — tracks which salt version was used

#### EF Core Integration

PII fields are annotated with `[Pii]` attribute and automatically encrypted/decrypted via EF Core value converters:

- `PiiEncryptionConverter` — encrypts/decrypts `EncryptedData` columns
- `BlindIndexValueConverter` — converts `BlindIndex` value objects to/from strings
- `PiiModelBuilderExtensions.ApplyPiiEncryption()` — auto-discovers and configures all PII properties

## Rate Limiting

### Policies

| Policy | Type | Limit | Window | Usage |
|---|---|---|---|---|
| **Global** | Fixed Window | 100 req/min per IP | 1 min | All endpoints |
| `AuthStrict` | Fixed Window | 5 req/min per IP | 1 min | Auth endpoints |
| `Webhook` | Fixed Window | 1000 req/min (pooled) | 1 min | Stripe webhooks |
| `PerUser` | Token Bucket | 60 burst, 10/10s refill | — | Basket, orders |
| `AdminStrict` | Fixed Window | 10 req/min per admin | 1 min | DLQ, finance, recovery |

### Rate Limit Response

```json
{
  "error": "Too many requests",
  "message": "Rate limit exceeded. Please try again later.",
  "retryAfter": 60
}
```

HTTP status: `429 Too Many Requests`

## Transport Security

### Kestrel Hardening

`AddEnterpriseWebHost()` configures:

| Setting | Value | Purpose |
|---|---|---|
| `AddServerHeader` | `false` | Hide server version |
| `AllowResponseHeaderCompression` | `true` | Performance |
| HTTP protocols | HTTP/1.1 + HTTP/2 + HTTP/3 | Modern transport |
| `MaxRequestBodySize` | 50 MB | DoS prevention |
| `RequestHeadersTimeout` | 30 seconds | Slowloris protection |

### HSTS

HTTP Strict Transport Security is enabled in non-development environments via `UseEnterpriseWebHost()`.

### CORS

Two CORS policies:

| Policy | Origins | Methods | Headers |
|---|---|---|---|
| `AllowConfigured` | From `Cors:AllowedOrigins` config | Any | Any + credentials |
| `StrictSameOrigin` | Primary origin only | GET, POST | Content-Type, Authorization |

Default allowed origins (dev): `https://localhost:5001`, `https://localhost:3000`.

## Build Hardening

### Compiler Security

From `Directory.Build.props`:

| Setting | Value | Purpose |
|---|---|---|
| `TreatWarningsAsErrors` | `true` (Release/CI) | No silent issues |
| `ControlFlowGuard` | `true` | Control-flow integrity |
| `Deterministic` | `true` | Reproducible builds |
| `NuGetAudit` | `true` | Vulnerability scanning |
| `NuGetAuditLevel` | `low` | Scan all severities |

### Native AOT Security

AOT compilation eliminates the JIT compiler from the deployed binary, reducing the attack surface:
- No `System.Reflection.Emit` available at runtime
- No dynamic code generation
- Smaller binary footprint
- `chiseled` Docker images: no shell, no package manager, non-root UID 1654

## Webhook Security

### Stripe Signature Verification

All incoming Stripe webhooks are verified using HMAC-SHA256 signatures:

```csharp
var stripeEvent = EventUtility.ConstructEvent(
    json,
    request.Headers["Stripe-Signature"],
    webhookSecret
);
```

Invalid signatures return `400 Bad Request` immediately.

### Webhook Idempotency

Processed webhook events are tracked in the `ProcessedWebhookEvent` entity. Duplicate deliveries are detected via `INSERT ... ON CONFLICT DO NOTHING` and return `200 OK` without reprocessing.

## Request Idempotency

Order creation requires an `X-Idempotency-Key` header (must be a valid GUID). The `IdempotencyFilter` validates the header and injects it into the `CreateOrderCommand`. Duplicate order submissions with the same key are deduplicated.

```http
POST /api/v1/orders
X-Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

## Antiforgery

Antiforgery protection is enabled via `builder.Services.AddAntiforgery()` and `app.UseAntiforgery()` for form-based submissions.

## Tenant Isolation

Multi-tenancy is implemented via `ITenantContext` and EF Core global query filters. Each `BaseDbContext` applies tenant isolation filters ensuring one tenant cannot access another tenant's data, even if direct SQL access were obtained.

## Security Checklist

| Control | Status |
|---|---|
| JWT validation (issuer, audience, lifetime) | Implemented |
| Token introspection (instant revocation) | Optional, configurable |
| Role-based access control (5 policies) | Implemented |
| Resource owner authorization | Implemented |
| Admin elevated authorization (role + key + IP) | Implemented |
| PII encryption at rest (AES-256-GCM) | Implemented |
| Blind indexes for searchable encrypted data | Implemented |
| Rate limiting (5 policies) | Implemented |
| Server header suppressed | Implemented |
| HSTS in production | Implemented |
| CORS with allowlist | Implemented |
| Request body size limits | 50 MB |
| Header timeout protection | 30 seconds |
| HTTP/3 support | Enabled |
| Antiforgery protection | Enabled |
| Webhook signature verification | Implemented |
| Webhook idempotency | Implemented |
| Request idempotency (orders) | Implemented |
| ControlFlowGuard | Enabled |
| NuGet vulnerability auditing | Enabled |
| TreatWarningsAsErrors (CI) | Enabled |
| Tenant data isolation | Implemented |

## Related Documentation

- [Architecture](ARCHITECTURE.md) — system design and middleware pipeline
- [API Reference](API_REFERENCE.md) — endpoint auth requirements
- [Webhook Reference](WEBHOOK_REFERENCE.md) — Stripe webhook security
- [Deployment](DEPLOYMENT.md) — production security configuration
- [Operations](OPERATIONS.md) — security monitoring
