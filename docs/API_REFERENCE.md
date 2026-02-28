# API Reference

Complete reference for all REST endpoints in NetCommerce. All versioned endpoints use URL-based versioning at `/api/v1/`. Admin endpoints are unversioned at `/api/admin/`.

## Authentication

All authenticated endpoints require a Bearer JWT token in the `Authorization` header, obtained via the `/api/v1/auth/token` endpoint or directly from Keycloak.

### Authorization Policies

| Policy | Description | Required Role |
|---|---|---|
| `AllowAnonymous` | No authentication required | — |
| `RequireAuthorization` | Any authenticated user | Any valid JWT |
| `CustomerOnly` | Customer role required | `customer` realm role |
| `VendorOnly` | Vendor/seller role required | `vendor` realm role |
| `AdminOnly` | Administrator role required | `admin` realm role |
| `AdminElevated` | Elevated admin role required | `admin` + elevated claim |

### Rate Limiting

| Policy | Scope | Limit |
|---|---|---|
| `PerUser` | Authenticated user | Standard rate per user |
| `AuthStrict` | Auth endpoints | Strict rate for login/token operations |
| `AdminStrict` | Admin endpoints | Strict rate for admin operations |

---

## Catalog Module

### Products

Base path: `/api/v1/products`

#### GET /api/v1/products/{id}

Retrieve a product by ID.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `ProductDto`

---

#### GET /api/v1/products/slug/{slug}

Retrieve a product by URL slug.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `slug` | `string` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `ProductDto`

---

#### GET /api/v1/products

List products with filtering and pagination.

| Parameter | Type | Source | Required | Default |
|---|---|---|---|---|
| `searchTerm` | `string` | Query | No | — |
| `categoryId` | `Guid` | Query | No | — |
| `minPrice` | `decimal` | Query | No | — |
| `maxPrice` | `decimal` | Query | No | — |
| `page` | `int` | Query | No | 1 |
| `pageSize` | `int` | Query | No | 20 |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `PaginatedResponse<ProductDto>`

```json
{
  "items": [...],
  "paginationMetadata": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8,
    "hasPreviousPage": false,
    "hasNextPage": true
  }
}
```

---

#### POST /api/v1/products

Create a new product.

**Auth:** VendorOnly

**Request Body:** `CreateProductCommand`

```json
{
  "title": "Premium Widget",
  "description": "High-quality widget",
  "sku": "WDG-001",
  "price": { "amount": 49.99, "currency": "GEL" },
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response:** `201 Created` with product ID

---

#### PUT /api/v1/products/{id}

Update an existing product.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Request Body:** `UpdateProductCommand`

**Response:** `200 OK`

---

#### PATCH /api/v1/products/{id}/price

Update product price. Uses `application/merge-patch+json` content type.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Request Body:**

```json
{
  "amount": 39.99,
  "currency": "GEL"
}
```

**Response:** `200 OK`

---

#### POST /api/v1/products/{id}/publish

Publish a draft product.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Response:** `200 OK`

---

#### POST /api/v1/products/{id}/images

Add an image to a product.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Request Body:**

```json
{
  "imageKey": "products/wdg-001-main.jpg",
  "displayOrder": 1,
  "isPrimary": true
}
```

**Response:** `200 OK`

---

#### DELETE /api/v1/products/{id}

Archive (soft delete) a product.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Response:** `200 OK`

---

### Categories

Base path: `/api/v1/categories`

#### GET /api/v1/categories

List all categories.

**Auth:** AllowAnonymous

**Response:** `200 OK` — `CategoryDto[]`

---

#### GET /api/v1/categories/{id}

Get category by ID.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `CategoryDto`

---

#### GET /api/v1/categories/slug/{slug}

Get category by slug.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `slug` | `string` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `CategoryDto`

---

#### GET /api/v1/categories/{id}/children

Get child categories.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `CategoryDto[]`

---

#### POST /api/v1/categories

Create a category.

**Auth:** VendorOnly

**Request Body:** `CreateCategoryCommand`

**Response:** `201 Created`

---

#### PUT /api/v1/categories/{id}

Update a category.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Response:** `200 OK`

---

#### DELETE /api/v1/categories/{id}

Delete a category.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Response:** `200 OK`

---

### Search

Base path: `/api/v1/products/search`

#### GET /api/v1/products/search

Full-text product search via MeiliSearch.

| Parameter | Type | Source | Required | Default |
|---|---|---|---|---|
| `query` | `string` | Query | No | — |
| `filter` | `string` | Query | No | — |
| `limit` | `int` | Query | No | 20 |
| `offset` | `int` | Query | No | 0 |

**Response:** `200 OK`

```json
{
  "query": "widget",
  "totalHits": 42,
  "limit": 20,
  "offset": 0,
  "processingTimeMs": 3,
  "results": [...],
  "facets": {
    "categories": { "Electronics": 15, "Tools": 27 },
    "price": { "0-50": 30, "50-100": 12 }
  }
}
```

---

## Basket Module

Base path: `/api/v1/basket`

All basket endpoints require authorization and apply per-user rate limiting.

#### GET /api/v1/basket

Get the current user's shopping basket.

**Auth:** RequireAuthorization

**Response:** `200 OK` — `ShoppingBasketDto`

---

#### POST /api/v1/basket/items

Add an item to the basket.

**Auth:** RequireAuthorization

**Request Body:**

```json
{
  "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "productName": "Premium Widget",
  "sku": "WDG-001",
  "quantity": 2,
  "unitPrice": 49.99,
  "imageUrl": "https://cdn.example.com/products/wdg-001.jpg"
}
```

**Response:** `200 OK`

---

#### PUT /api/v1/basket/items/{productId}

Update item quantity.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `productId` | `Guid` | Route | Yes |

**Auth:** RequireAuthorization

**Request Body:**

```json
{
  "quantity": 3
}
```

**Response:** `200 OK`

---

#### DELETE /api/v1/basket/items/{productId}

Remove an item from the basket.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `productId` | `Guid` | Route | Yes |

**Auth:** RequireAuthorization

**Response:** `200 OK`

---

#### DELETE /api/v1/basket

Clear the entire basket.

**Auth:** RequireAuthorization

**Response:** `200 OK`

---

## Ordering Module

Base path: `/api/v1/orders`

#### POST /api/v1/orders

Create a new order. Requires an idempotency key to prevent duplicate submissions.

| Header | Type | Required |
|---|---|---|
| `X-Idempotency-Key` | `Guid` | Yes |

**Auth:** CustomerOnly + PerUser rate limit

**Request Body:** `CreateOrderCommand`

**Response:** `202 Accepted` — Order ID

The `IdempotencyFilter` validates the header is present and is a valid GUID. The key is injected into the `CreateOrderCommand` for duplicate detection.

---

#### GET /api/v1/orders/manual-intervention

List orders requiring manual intervention (saga state = `ManualInterventionRequired`).

**Auth:** AdminOnly

**Response:** `200 OK` — Order list

---

## Inventory Module

Base path: `/api/v1/inventory`

#### GET /api/v1/inventory/product/{productId}

Get stock information for a product.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `productId` | `Guid` | Route | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — `StockDto`

---

#### GET /api/v1/inventory/low-stock

List items below their low-stock threshold.

**Auth:** VendorOnly

**Response:** `200 OK` — `StockDto[]`

---

#### POST /api/v1/inventory

Create a stock entry.

**Auth:** VendorOnly

**Request Body:**

```json
{
  "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sku": "WDG-001",
  "initialQuantity": 100,
  "lowStockThreshold": 10,
  "warehouseLocation": "Warehouse A, Shelf 3"
}
```

**Response:** `201 Created`

---

#### PATCH /api/v1/inventory/{stockId}/quantity

Adjust stock quantity.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `stockId` | `Guid` | Route | Yes |

**Auth:** VendorOnly

**Request Body:**

```json
{
  "quantityDelta": 50,
  "reason": "Restocking shipment received"
}
```

**Response:** `200 OK`

---

#### POST /api/v1/inventory/reserve

Reserve stock for an order.

**Auth:** RequireAuthorization

**Request Body:** `ReserveStockCommand`

**Response:** `200 OK`

---

#### POST /api/v1/inventory/products/{productId}/reservations/{reservationId}/confirm

Confirm a stock reservation (permanent deduction).

| Parameter | Type | Source | Required |
|---|---|---|---|
| `productId` | `Guid` | Route | Yes |
| `reservationId` | `Guid` | Route | Yes |

**Auth:** RequireAuthorization

**Response:** `200 OK`

---

#### POST /api/v1/inventory/products/{productId}/reservations/{reservationId}/release

Release a stock reservation.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `productId` | `Guid` | Route | Yes |
| `reservationId` | `Guid` | Route | Yes |

**Auth:** RequireAuthorization

**Response:** `200 OK`

---

## Media Module

Base path: `/api/v1/media`

#### GET /api/v1/media/upload-url

Generate a presigned upload URL.

| Parameter | Type | Source | Required | Default |
|---|---|---|---|---|
| `fileName` | `string` | Query | Yes | — |
| `contentType` | `string` | Query | Yes | — |
| `folder` | `string` | Query | No | `products` |
| `expiryMinutes` | `int` | Query | No | 15 |

**Auth:** VendorOnly

**Response:** `200 OK` — Presigned URL string

---

#### POST /api/v1/media/upload

Upload a file directly.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `file` | `IFormFile` | Form | Yes |
| `folder` | `string` | Query | No |

**Auth:** VendorOnly (DisableAntiforgery)

**Constraints:**
- Maximum file size: 10 MB
- Allowed types: `image/jpeg`, `image/png`, `image/webp`, `image/gif`

**Response:** `200 OK` — Upload result with blob key

---

#### DELETE /api/v1/media

Delete a media file.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `key` | `string` | Query | Yes |

**Auth:** VendorOnly

**Response:** `200 OK`

---

#### GET /api/v1/media/url

Get a public URL for a media file.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `key` | `string` | Query | Yes |

**Auth:** AllowAnonymous

**Response:** `200 OK` — URL string

---

## Auth Module

Base path: `/api/v1/auth`

BFF (Backend-for-Frontend) endpoints for Keycloak token management. All token operations are proxied through the API to avoid exposing Keycloak directly.

#### POST /api/v1/auth/token

Exchange credentials for tokens.

**Auth:** AllowAnonymous + AuthStrict rate limit

**Request Body:**

```json
{
  "grantType": "authorization_code",
  "code": "auth-code-from-redirect",
  "codeVerifier": "pkce-verifier",
  "redirectUri": "https://app.example.com/callback",
  "clientId": "netcommerce-web"
}
```

**Supported Grant Types:**
- `authorization_code` — PKCE-based code exchange (recommended)
- `client_credentials` — service-to-service authentication
- `password` — **rejected** (ROPC disabled for security)

**Response:** `200 OK` — Token response from Keycloak

---

#### POST /api/v1/auth/refresh

Refresh an access token.

**Auth:** AllowAnonymous + AuthStrict rate limit

**Request Body:**

```json
{
  "refreshToken": "refresh-token-value"
}
```

**Response:** `200 OK` — New token pair

---

#### POST /api/v1/auth/revoke

Revoke a token.

**Auth:** AllowAnonymous + AuthStrict rate limit

**Request Body:**

```json
{
  "token": "token-to-revoke",
  "tokenTypeHint": "refresh_token"
}
```

**Response:** `200 OK`

---

#### POST /api/v1/auth/logout

Logout and revoke refresh token.

**Auth:** AllowAnonymous + AuthStrict rate limit

**Request Body:**

```json
{
  "refreshToken": "refresh-token-value"
}
```

**Response:** `200 OK`

---

#### GET /api/v1/auth/session

Get current session information.

**Auth:** RequireAuthorization

**Response:** `200 OK`

```json
{
  "userId": "user-uuid",
  "username": "john.doe",
  "email": "john@example.com",
  "realmRoles": ["customer"],
  "clientRoles": [],
  "tenantId": "tenant-uuid",
  "tokenExpiresAt": "2025-01-01T12:00:00Z",
  "authenticatedAt": "2025-01-01T11:00:00Z",
  "sessionState": "active"
}
```

---

## Webhook Module

Base path: `/api/webhooks`

#### POST /api/webhooks/stripe

Process Stripe webhook events. See [WEBHOOK_REFERENCE.md](WEBHOOK_REFERENCE.md) for the complete webhook specification.

**Auth:** AllowAnonymous + DisableAntiforgery (signature-verified)

**Response:** `200 OK` — `{ status: "processed", eventId: "evt_..." }`

---

## Admin Module

All admin endpoints require `AdminElevated` authorization and `AdminStrict` rate limiting.

### Dead Letter Queue

Base path: `/api/admin/dlq`

#### GET /api/admin/dlq

List dead-lettered messages.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `limit` | `int` | Query | No |
| `offset` | `int` | Query | No |
| `type` | `string` | Query | No |

**Response:** `200 OK` — Dead letter message list

---

#### POST /api/admin/dlq/{id}/replay

Replay a dead-lettered message.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Response:** `200 OK`

---

#### DELETE /api/admin/dlq/{id}

Discard a dead-lettered message.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `id` | `Guid` | Route | Yes |

**Response:** `200 OK`

---

#### POST /api/admin/dlq/bulk-replay

Bulk replay dead-lettered messages.

**Request Body:**

```json
{
  "messageTypeFilter": "ProcessExternalPaymentConfirmation",
  "limit": 200
}
```

**Response:** `200 OK`

---

### Finance Administration

Base path: `/api/admin/finance`

#### GET /api/admin/finance/reconciliation-sessions

List reconciliation sessions.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `startDate` | `DateTime` | Query | No |
| `endDate` | `DateTime` | Query | No |
| `status` | `ReconciliationStatus` | Query | No |
| `page` | `int` | Query | No |
| `pageSize` | `int` | Query | No |

**Response:** `200 OK` — Paginated reconciliation sessions

---

#### GET /api/admin/finance/reconciliation-sessions/{sessionId}

Get reconciliation session details.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `sessionId` | `Guid` | Route | Yes |

**Response:** `200 OK` — Session with discrepancies

---

#### POST /api/admin/finance/reconciliation-sessions/trigger

Trigger a manual reconciliation.

**Request Body:**

```json
{
  "date": "2025-01-15"
}
```

**Response:** `202 Accepted`

---

#### POST /api/admin/finance/discrepancies/resolve

Resolve a reconciliation discrepancy.

**Request Body:**

```json
{
  "sessionId": "session-guid",
  "externalTxnId": "pi_xxx",
  "action": "AcceptDiscrepancy",
  "reason": "Timing difference, resolved on next day"
}
```

**Actions:** `CreateShadowOrder`, `RefundGhostCharge`, `AcceptDiscrepancy`, `InvestigateFurther`

**Response:** `200 OK`

---

#### GET /api/admin/finance/alerts/mismatched-sessions

Get sessions with unresolved discrepancies.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `since` | `DateTime` | Query | No |

**Response:** `200 OK`

---

### Order Recovery

Base path: `/api/admin/orders`

#### POST /api/admin/orders/{orderId}/force-complete

Force-complete a stuck saga.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `orderId` | `Guid` | Route | Yes |

**Request Body:**

```json
{
  "reason": "Manual verification confirmed payment received",
  "notes": "Stripe dashboard shows successful charge"
}
```

**Response:** `200 OK`

---

#### POST /api/admin/orders/{orderId}/override-payment-status

Override payment status for a stuck order.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `orderId` | `Guid` | Route | Yes |

**Request Body:**

```json
{
  "paymentStatus": "Succeeded",
  "stripeChargeId": "ch_xxx",
  "reason": "Webhook delivery delayed, manual verification"
}
```

**Response:** `200 OK`

---

#### POST /api/admin/orders/{orderId}/force-cancel

Force-cancel an order with optional refund.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `orderId` | `Guid` | Route | Yes |

**Request Body:**

```json
{
  "reason": "Customer requested cancellation after timeout",
  "refundAmount": 49.99,
  "notifyCustomer": true
}
```

**Response:** `200 OK`

---

#### POST /api/admin/orders/{orderId}/retry-step

Retry a specific saga step.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `orderId` | `Guid` | Route | Yes |

**Request Body:**

```json
{
  "step": "ProcessingPayment"
}
```

**Response:** `200 OK`

---

#### GET /api/admin/orders/{orderId}/saga-details

Get full saga state for an order.

| Parameter | Type | Source | Required |
|---|---|---|---|
| `orderId` | `Guid` | Route | Yes |

**Response:** `200 OK` — Full saga state including all tracking flags

---

#### POST /api/admin/orders/bulk-retry-stuck

Bulk retry stuck sagas in a specific state.

**Request Body:**

```json
{
  "sagaState": "ProcessingPayment",
  "maxOrdersToRetry": 100
}
```

**Response:** `200 OK`

---

## Error Responses

All endpoints return RFC 9457 Problem Details for errors:

```json
{
  "type": "https://netcommerce.example.com/errors/validation",
  "title": "Validation Error",
  "status": 422,
  "detail": "Title is required"
}
```

### Standard Error Codes

| Status | Type | Description |
|---|---|---|
| 400 | Bad Request | Malformed request |
| 401 | Unauthorized | Missing or invalid JWT |
| 403 | Forbidden | Insufficient role/permissions |
| 404 | Not Found | Resource does not exist |
| 409 | Conflict | Concurrency conflict (stale xmin) |
| 422 | Validation Error | Business rule violation |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Error | Unexpected server error |

## Health Endpoints

| Endpoint | Purpose |
|---|---|
| `GET /health/ready` | Readiness probe (all dependencies available) |
| `GET /health/live` | Liveness probe (process running) |

## Real-Time

| Endpoint | Protocol | Purpose |
|---|---|---|
| `/api/messages` | SignalR WebSocket | Order status notifications |

## Related Documentation

- [Webhook Reference](WEBHOOK_REFERENCE.md) — Stripe webhook specification
- [Architecture](ARCHITECTURE.md) — endpoint organization
- [Security](SECURITY.md) — authentication and rate limiting
