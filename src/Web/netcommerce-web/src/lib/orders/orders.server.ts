import { apiFetch } from '@/lib/api/client.server';

/**
 * Server-side writes for orders.
 *
 * Everything here runs on the server and goes through `apiFetch`, so the bearer
 * token stays in the server session — same rule as catalog.server.ts.
 *
 * Sources:
 *   src/Api/Endpoints/Ordering/OrderEndpoints.cs
 *     (POST /api/v1/orders + IdempotencyFilter, DELETE /api/v1/orders/{id})
 *   src/Api/Endpoints/Common/IdempotencyFilter.cs
 *     (X-Idempotency-Key must be a GUID; the filter injects it into the command)
 *   src/Ordering/Ordering.Infrastructure/Handlers/OrderCommandHandlers.cs
 *     (server-side pricing: ExpectedPrice is a guard, never a charge figure)
 */

export interface OrderItemInput {
  productId: string;
  quantity: number;
  /**
   * The unit price the basket showed, echoed back so the server can reject a
   * checkout when the catalog price moved underneath it (409 Conflict).
   * Never a charge figure — pricing is resolved server-side from the catalog.
   */
  expectedPrice?: number;
}

export interface OrderAddressInput {
  street: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  recipientName: string;
  phoneNumber: string;
}

export interface CreateOrderInput {
  customerEmail: string;
  customerName: string;
  items: OrderItemInput[];
  shippingAddress: OrderAddressInput;
  billingAddress: OrderAddressInput;
  paymentMethod: string;
  couponCode?: string;
}

/** POST /api/v1/orders — CustomerOnly + PerUser rate limit. Returns the order id. */
export async function createOrder(
  input: CreateOrderInput,
  idempotencyKey?: string,
): Promise<{ id: string }> {
  return apiFetch<{ id: string }>('/api/v1/orders', {
    method: 'POST',
    body: {
      // Placeholder: the endpoint overwrites CustomerId from the JWT subject
      // (accepting a client-supplied id would let customers order as each other).
      // Sent explicitly rather than omitted so the body matches the command shape.
      customerId: '00000000-0000-0000-0000-000000000000',
      customerEmail: input.customerEmail,
      customerName: input.customerName,
      items: input.items.map((item) => ({
        productId: item.productId,
        quantity: item.quantity,
        ...(item.expectedPrice === undefined
          ? {}
          : { expectedPrice: item.expectedPrice }),
      })),
      shippingAddress: input.shippingAddress,
      billingAddress: input.billingAddress,
      paymentMethod: input.paymentMethod,
      // The IdempotencyFilter overwrites this from the X-Idempotency-Key header
      // (which apiFetch always sends on mutations, minting one when the caller
      // did not supply it), so the body value is never authoritative — it only
      // satisfies the record shape. Echo the caller key when known.
      idempotencyKey: idempotencyKey ?? '00000000-0000-0000-0000-000000000000',
      ...(input.couponCode ? { couponCode: input.couponCode } : {}),
    },
    cache: 'no-store',
    idempotencyKey,
  });
}

export interface CancelOrderResult {
  id: string;
  message: string;
}

/** DELETE /api/v1/orders/{id} — CustomerOnly; only the owning customer may cancel. */
export async function cancelOrder(
  orderId: string,
  reason?: string,
  idempotencyKey?: string,
): Promise<CancelOrderResult> {
  const query = reason ? `?reason=${encodeURIComponent(reason)}` : '';
  return apiFetch<CancelOrderResult>(
    `/api/v1/orders/${encodeURIComponent(orderId)}${query}`,
    { method: 'DELETE', cache: 'no-store', idempotencyKey },
  );
}
