# NetCommerce API Reference

> **Complete REST API documentation for the NetCommerce e-commerce platform**

---

## Table of Contents

1. [API Overview](#api-overview)
2. [Authentication](#authentication)
3. [Common Patterns](#common-patterns)
4. [Products API](#products-api)
5. [Categories API](#categories-api)
6. [Search API](#search-api)
7. [Basket API](#basket-api)
8. [Orders API](#orders-api)
9. [Inventory API](#inventory-api)
10. [Payments API](#payments-api)
11. [Media API](#media-api)
12. [Admin API](#admin-api)
13. [Error Handling](#error-handling)
14. [Rate Limiting](#rate-limiting)
15. [Webhooks](#webhooks)

---

## API Overview

### Base URL

```
Production:  https://api.netcommerce.io
Staging:     https://staging-api.netcommerce.io
Development: https://localhost:{port}
```

### API Versioning

NetCommerce uses URL path versioning:

```
GET /api/v1/products
GET /api/v2/products  (future)
```

**Current Version:** `v1`

### Content Types

| Request | Response |
|---------|----------|
| `application/json` | `application/json` |
| `application/merge-patch+json` (PATCH) | `application/problem+json` (errors) |

### OpenAPI / Swagger

Interactive API documentation is available at:
- **Swagger UI:** `{base_url}/swagger`
- **OpenAPI Spec:** `{base_url}/swagger/v1/swagger.json`

---

## Authentication

### OAuth 2.0 / OpenID Connect

NetCommerce uses Keycloak as the identity provider with OAuth 2.0 and OpenID Connect.

### Obtaining Tokens

**Authorization Code Flow (Recommended for web apps):**
```
GET https://{keycloak}/realms/netcommerce/protocol/openid-connect/auth
    ?client_id=netcommerce-web
    &redirect_uri=https://app.netcommerce.io/callback
    &response_type=code
    &scope=openid profile email netcommerce.api
```

**Client Credentials Flow (Service-to-service):**
```bash
curl -X POST https://{keycloak}/realms/netcommerce/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=your-service" \
  -d "client_secret=your-secret" \
  -d "scope=netcommerce.api"
```

### Using Tokens

Include the access token in the Authorization header:

```http
GET /api/v1/orders HTTP/1.1
Host: api.netcommerce.io
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

### User Roles

| Role | Scope | Description |
|------|-------|-------------|
| `customer` | Browse, cart, checkout | Standard customer access |
| `vendor` | Products, inventory | Product management |
| `admin` | Full access | System administration |

### Test Accounts

| Role | Email | Password |
|------|-------|----------|
| Admin | admin@netcommerce.com | Admin123! |
| Vendor | vendor@netcommerce.com | Vendor123! |
| Customer | customer@netcommerce.com | Customer123! |

---

## Common Patterns

### Pagination

All list endpoints support cursor-based pagination:

**Request:**
```http
GET /api/v1/products?page=1&pageSize=20
```

**Response:**
```json
{
  "items": [...],
  "page": 1,
  "pageSize": 20,
  "totalCount": 150,
  "totalPages": 8,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

### Filtering

Query parameters for filtering:

```http
GET /api/v1/products?categoryId=abc&minPrice=10&maxPrice=100&searchTerm=laptop
```

### Sorting

```http
GET /api/v1/products?sortBy=price&sortDirection=desc
```

### Idempotency

Critical mutation endpoints require idempotency keys:

```http
POST /api/v1/orders HTTP/1.1
X-Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{ "items": [...] }
```

**Behavior:**
- First request: Processed normally
- Subsequent requests (same key): Returns cached response
- Key TTL: 24 hours

### Optimistic Concurrency

Update operations require version checking via ETag:

```http
GET /api/v1/products/123
# Response includes: ETag: "5"

PUT /api/v1/products/123
If-Match: "5"
Content-Type: application/json

{ ... }
```

**Conflict Response (409):**
```json
{
  "type": "https://httpstatuses.com/409",
  "title": "Conflict",
  "status": 409,
  "detail": "Resource was modified by another request"
}
```

---

## Products API

### List Products

```http
GET /api/v1/products
```

**Query Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `page` | int | 1 | Page number |
| `pageSize` | int | 20 | Items per page (max: 100) |
| `searchTerm` | string | - | Full-text search |
| `categoryId` | guid | - | Filter by category |
| `minPrice` | decimal | - | Minimum price |
| `maxPrice` | decimal | - | Maximum price |

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "Laptop Pro 15",
      "slug": "laptop-pro-15",
      "description": "High-performance laptop...",
      "price": {
        "amount": 1299.99,
        "currency": "GEL"
      },
      "categoryId": "...",
      "categoryName": "Electronics",
      "status": "Published",
      "images": [
        {
          "url": "https://cdn.netcommerce.io/products/123/main.jpg",
          "altText": "Laptop front view",
          "isPrimary": true
        }
      ],
      "createdAt": "2026-01-15T10:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 150
}
```

### Get Product by ID

```http
GET /api/v1/products/{id}
```

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Laptop Pro 15",
  "slug": "laptop-pro-15",
  "description": "High-performance laptop with 16GB RAM...",
  "price": {
    "amount": 1299.99,
    "currency": "GEL"
  },
  "categoryId": "...",
  "categoryName": "Electronics",
  "status": "Published",
  "attributes": [
    { "name": "RAM", "value": "16GB" },
    { "name": "Storage", "value": "512GB SSD" }
  ],
  "images": [...],
  "createdAt": "2026-01-15T10:30:00Z",
  "updatedAt": "2026-01-20T14:00:00Z"
}
```

### Get Product by Slug

```http
GET /api/v1/products/slug/{slug}
```

**Example:**
```http
GET /api/v1/products/slug/laptop-pro-15
```

### Create Product

🔒 **Requires:** `vendor` role

```http
POST /api/v1/products
Content-Type: application/json

{
  "name": "New Product",
  "description": "Product description...",
  "price": 99.99,
  "currency": "GEL",
  "categoryId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "attributes": [
    { "name": "Color", "value": "Blue" }
  ]
}
```

**Response:** `201 Created`
```http
Location: /api/v1/products/550e8400-e29b-41d4-a716-446655440000

{
  "id": "550e8400-e29b-41d4-a716-446655440000"
}
```

### Update Product

🔒 **Requires:** `vendor` role

```http
PUT /api/v1/products/{id}
Content-Type: application/json
If-Match: "5"

{
  "name": "Updated Product Name",
  "description": "Updated description...",
  "price": 149.99,
  "currency": "GEL",
  "categoryId": "..."
}
```

**Response:** `204 No Content`

### Update Product Price

🔒 **Requires:** `vendor` role

```http
PATCH /api/v1/products/{id}/price
Content-Type: application/merge-patch+json

{
  "price": 129.99
}
```

**Response:** `204 No Content`

### Publish Product

🔒 **Requires:** `vendor` role

```http
POST /api/v1/products/{id}/publish
```

**Response:** `204 No Content`

### Delete Product

🔒 **Requires:** `vendor` role

```http
DELETE /api/v1/products/{id}
```

**Response:** `204 No Content`

---

## Categories API

### List Categories

```http
GET /api/v1/categories
```

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "...",
      "name": "Electronics",
      "slug": "electronics",
      "parentId": null,
      "children": [
        {
          "id": "...",
          "name": "Laptops",
          "slug": "laptops"
        }
      ]
    }
  ]
}
```

### Get Category

```http
GET /api/v1/categories/{id}
```

### Create Category

🔒 **Requires:** `vendor` role

```http
POST /api/v1/categories
Content-Type: application/json

{
  "name": "New Category",
  "parentId": null
}
```

---

## Search API

### Full-Text Search

Powered by Meilisearch with <50ms response times.

```http
GET /api/v1/search?q=laptop&limit=20
```

**Query Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `q` | string | - | Search query |
| `limit` | int | 20 | Max results |
| `offset` | int | 0 | Pagination offset |
| `filter` | string | - | Filter expression |
| `facets` | string[] | - | Facets to compute |

**Response:** `200 OK`
```json
{
  "hits": [
    {
      "id": "...",
      "name": "Laptop Pro 15",
      "description": "...",
      "price": 1299.99,
      "_matchesPosition": {
        "name": [{ "start": 0, "length": 6 }]
      }
    }
  ],
  "query": "laptop",
  "processingTimeMs": 12,
  "limit": 20,
  "offset": 0,
  "estimatedTotalHits": 45,
  "facetDistribution": {
    "category": {
      "Electronics": 30,
      "Accessories": 15
    }
  }
}
```

### Autocomplete

```http
GET /api/v1/search/autocomplete?q=lap&limit=5
```

---

## Basket API

### Get Basket

```http
GET /api/v1/basket
```

**Response:** `200 OK`
```json
{
  "id": "customer-123",
  "items": [
    {
      "productId": "...",
      "productName": "Laptop Pro 15",
      "quantity": 1,
      "unitPrice": {
        "amount": 1299.99,
        "currency": "GEL"
      },
      "totalPrice": {
        "amount": 1299.99,
        "currency": "GEL"
      }
    }
  ],
  "totalAmount": {
    "amount": 1299.99,
    "currency": "GEL"
  },
  "itemCount": 1
}
```

### Add Item to Basket

🔒 **Requires:** `customer` role

```http
POST /api/v1/basket/items
Content-Type: application/json

{
  "productId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "quantity": 2
}
```

**Response:** `200 OK` (returns updated basket)

### Update Item Quantity

🔒 **Requires:** `customer` role

```http
PUT /api/v1/basket/items/{productId}
Content-Type: application/json

{
  "quantity": 3
}
```

### Remove Item from Basket

🔒 **Requires:** `customer` role

```http
DELETE /api/v1/basket/items/{productId}
```

### Clear Basket

🔒 **Requires:** `customer` role

```http
DELETE /api/v1/basket
```

---

## Orders API

### Create Order

🔒 **Requires:** `customer` role

```http
POST /api/v1/orders
X-Idempotency-Key: {uuid}
Content-Type: application/json

{
  "shippingAddress": {
    "street": "123 Main St",
    "city": "Tbilisi",
    "state": "Tbilisi",
    "postalCode": "0105",
    "country": "GE"
  },
  "billingAddress": {
    "street": "123 Main St",
    "city": "Tbilisi",
    "state": "Tbilisi",
    "postalCode": "0105",
    "country": "GE"
  },
  "notes": "Please leave at the door"
}
```

**Response:** `201 Created`
```http
Location: /api/v1/orders/550e8400-e29b-41d4-a716-446655440000

{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "orderNumber": "ORD-2026-00001"
}
```

**Order Creation Flow:**
1. Validates basket has items
2. Creates order with "Submitted" status
3. Initiates OrderFulfillmentSaga:
   - Reserves inventory (soft lock)
   - Processes payment
   - Confirms inventory (hard deduction)
4. On success: Order marked "Paid"
5. On failure: Compensating transactions run

### Get Order

🔒 **Requires:** Order owner or `admin` role

```http
GET /api/v1/orders/{id}
```

**Response:** `200 OK`
```json
{
  "id": "...",
  "orderNumber": "ORD-2026-00001",
  "status": "Paid",
  "customerId": "...",
  "items": [
    {
      "productId": "...",
      "productName": "Laptop Pro 15",
      "appliedPrice": {
        "amount": 1299.99,
        "currency": "GEL"
      },
      "quantity": 1,
      "totalPrice": {
        "amount": 1299.99,
        "currency": "GEL"
      }
    }
  ],
  "totalAmount": {
    "amount": 1299.99,
    "currency": "GEL"
  },
  "shippingAddress": {...},
  "billingAddress": {...},
  "createdAt": "2026-02-01T10:00:00Z",
  "paidAt": "2026-02-01T10:01:30Z",
  "paymentTransactionId": "pi_abc123"
}
```

### List Orders

🔒 **Requires:** `customer` role (sees own orders) or `admin` (sees all)

```http
GET /api/v1/orders?page=1&pageSize=10&status=Paid
```

### Cancel Order

🔒 **Requires:** Order owner (within grace period) or `admin`

```http
POST /api/v1/orders/{id}/cancel
Content-Type: application/json

{
  "reason": "Changed my mind"
}
```

**Cancellation Rules:**
- Customers can cancel within 15-minute grace period
- Admins can cancel any time
- Triggers refund if payment was processed

---

## Inventory API

### Get Stock Level

```http
GET /api/v1/inventory/{productId}
```

**Response:** `200 OK`
```json
{
  "productId": "...",
  "availableQuantity": 50,
  "reservedQuantity": 5,
  "totalQuantity": 55,
  "reorderPoint": 10,
  "isLowStock": false,
  "lastRestockedAt": "2026-01-20T09:00:00Z"
}
```

### Update Stock

🔒 **Requires:** `vendor` role

```http
PUT /api/v1/inventory/{productId}
Content-Type: application/json

{
  "quantity": 100,
  "reason": "Restock from supplier"
}
```

### Bulk Stock Update

🔒 **Requires:** `vendor` role

```http
POST /api/v1/inventory/bulk
Content-Type: application/json

{
  "items": [
    { "productId": "...", "quantity": 50, "reason": "Restock" },
    { "productId": "...", "quantity": 30, "reason": "Restock" }
  ]
}
```

---

## Payments API

### Payment Webhooks

Stripe webhook endpoint (called by Stripe):

```http
POST /api/v1/payments/webhooks/stripe
Stripe-Signature: t=1234567890,v1=...
Content-Type: application/json

{
  "type": "payment_intent.succeeded",
  "data": {
    "object": {
      "id": "pi_abc123",
      ...
    }
  }
}
```

### Get Payment Status

🔒 **Requires:** Order owner or `admin` role

```http
GET /api/v1/payments/orders/{orderId}
```

**Response:** `200 OK`
```json
{
  "orderId": "...",
  "status": "Succeeded",
  "transactionId": "pi_abc123",
  "amount": {
    "amount": 1299.99,
    "currency": "GEL"
  },
  "processedAt": "2026-02-01T10:01:30Z"
}
```

---

## Media API

### Get Upload URL

🔒 **Requires:** `vendor` role

```http
POST /api/v1/media/upload-url
Content-Type: application/json

{
  "fileName": "product-image.jpg",
  "contentType": "image/jpeg",
  "sizeBytes": 2048000
}
```

**Response:** `200 OK`
```json
{
  "uploadUrl": "https://storage.blob.core.windows.net/...",
  "mediaId": "550e8400-e29b-41d4-a716-446655440000",
  "expiresAt": "2026-02-01T11:00:00Z"
}
```

**Upload Flow:**
1. Request pre-signed URL
2. Upload file directly to blob storage using returned URL
3. Confirm upload completion

### Confirm Upload

🔒 **Requires:** `vendor` role

```http
POST /api/v1/media/{mediaId}/confirm
```

**Response:** `200 OK`
```json
{
  "mediaId": "...",
  "publicUrl": "https://cdn.netcommerce.io/media/...",
  "contentType": "image/jpeg",
  "sizeBytes": 2048000
}
```

---

## Admin API

### Get Stuck Sagas

🔒 **Requires:** `admin` role

Returns orders requiring manual intervention (e.g., refund failed).

```http
GET /api/v1/orders/manual-intervention
```

**Response:** `200 OK`
```json
{
  "count": 2,
  "sagas": [
    {
      "orderId": "...",
      "orderNumber": "ORD-2026-00001",
      "paymentTransactionId": "pi_abc123",
      "refundFailureReason": "Stripe API timeout",
      "stuckSince": "2026-02-01T10:05:00Z",
      "amount": {
        "amount": 1299.99,
        "currency": "GEL"
      }
    }
  ]
}
```

### Resolve Stuck Saga

🔒 **Requires:** `admin` role

```http
POST /api/v1/admin/sagas/{orderId}/resolve
Content-Type: application/json

{
  "resolution": "RefundManually",
  "notes": "Refund issued manually via Stripe dashboard"
}
```

### Run Reconciliation

🔒 **Requires:** `admin` role

```http
POST /api/v1/admin/reconciliation/run
```

---

## Error Handling

### RFC 9457 Problem Details

All errors follow the RFC 9457 Problem Details format:

```json
{
  "type": "https://httpstatuses.com/422",
  "title": "Validation Error",
  "status": 422,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/v1/products",
  "errors": {
    "name": ["Product name is required"],
    "price": ["Price must be greater than 0"]
  }
}
```

### Common Error Codes

| Status | Type | Description |
|--------|------|-------------|
| 400 | Bad Request | Malformed request syntax |
| 401 | Unauthorized | Missing or invalid authentication |
| 403 | Forbidden | Insufficient permissions |
| 404 | Not Found | Resource doesn't exist |
| 409 | Conflict | Concurrency conflict or state violation |
| 422 | Unprocessable Entity | Validation failed |
| 429 | Too Many Requests | Rate limit exceeded |
| 500 | Internal Server Error | Unexpected server error |

### Domain-Specific Errors

```json
{
  "type": "https://netcommerce.io/errors/insufficient-stock",
  "title": "Insufficient Stock",
  "status": 422,
  "detail": "Product 'Laptop Pro 15' only has 3 units available",
  "productId": "...",
  "requestedQuantity": 5,
  "availableQuantity": 3
}
```

---

## Rate Limiting

### Limits

| Endpoint Type | Limit | Window |
|--------------|-------|--------|
| Anonymous | 100 requests | 1 minute |
| Authenticated | 1000 requests | 1 minute |
| Search | 60 requests | 1 minute |
| Webhooks | 10000 requests | 1 minute |

### Headers

```http
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 950
X-RateLimit-Reset: 1706788800
```

### Rate Limit Response

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 30

{
  "type": "https://httpstatuses.com/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please retry after 30 seconds."
}
```

---

## Webhooks

### Webhook Events

NetCommerce can send webhooks for significant events:

| Event | Description |
|-------|-------------|
| `order.created` | New order submitted |
| `order.paid` | Payment confirmed |
| `order.shipped` | Order dispatched |
| `order.delivered` | Order delivered |
| `order.cancelled` | Order cancelled |
| `inventory.low_stock` | Stock below reorder point |
| `payment.failed` | Payment attempt failed |
| `refund.completed` | Refund processed |

### Webhook Payload

```json
{
  "id": "evt_123",
  "type": "order.paid",
  "createdAt": "2026-02-01T10:01:30Z",
  "data": {
    "orderId": "...",
    "orderNumber": "ORD-2026-00001",
    "amount": {
      "amount": 1299.99,
      "currency": "GEL"
    }
  }
}
```

### Webhook Security

Webhooks include HMAC signature for verification:

```http
X-NetCommerce-Signature: sha256=abc123...
X-NetCommerce-Timestamp: 1706788800
```

**Verification:**
```csharp
var payload = $"{timestamp}.{body}";
var expectedSignature = ComputeHmacSha256(payload, webhookSecret);
var isValid = signature == expectedSignature;
```

---

## SDK Support

### Official SDKs

- **.NET**: `NetCommerce.Client` (NuGet)
- **JavaScript/TypeScript**: `@netcommerce/client` (npm)
- **Python**: `netcommerce` (PyPI)

### .NET SDK Example

```csharp
using NetCommerce.Client;

var client = new NetCommerceClient(
    baseUrl: "https://api.netcommerce.io",
    accessToken: "your-token");

// Get products
var products = await client.Products.ListAsync(
    categoryId: categoryId,
    pageSize: 20);

// Create order
var order = await client.Orders.CreateAsync(new CreateOrderRequest
{
    ShippingAddress = new Address { ... }
});
```

---

**Document Version:** 1.0
**Last Updated:** February 2026
**API Version:** v1
**Maintainer:** NetCommerce API Team
