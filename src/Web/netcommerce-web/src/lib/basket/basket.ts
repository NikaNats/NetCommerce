/**
 * Basket contract, transcribed from the server — not guessed.
 *
 * Sources:
 *   src/Basket/Basket.Application/ShoppingBasket.cs
 *   src/Api/Endpoints/Basket/BasketEndpoints.cs
 *
 * Route prefix `/api/v1/basket`, and the whole group is `.RequireAuthorization()`
 * plus `.RequireRateLimiting("PerUser")` — so every call here needs a session.
 */

export interface BasketItem {
  productId: string;
  productName: string;
  /** OMITTED when null — the API uses DefaultIgnoreCondition = WhenWritingNull. */
  sku?: string;
  price: number;
  quantity: number;
  /** OMITTED when null — see sku. */
  imageUrl?: string;
}

/**
 * GET /api/v1/basket — the server returns the ShoppingBasket itself.
 *
 * `totalPrice` is a server-side computed property (Sum of price × quantity), so
 * it arrives populated. It is NOT recomputed here: the client's arithmetic could
 * disagree with the server's (float drift, or a tampered payload), and checkout
 * must agree with what the server will charge.
 */
export interface Basket {
  customerId: string;
  items: BasketItem[];
  createdAt: string;
  lastUpdatedAt: string;
  totalPrice: number;
}

/** POST /api/v1/basket/items — AddBasketItemRequest */
export interface AddBasketItemRequest {
  productId: string;
  productName: string;
  sku?: string;
  quantity: number;
  unitPrice: number;
  imageUrl?: string;
}

/**
 * Server-side quantity floor.
 *
 * ShoppingBasket.UpdateItemQuantity removes the item when quantity <= 0, so a
 * "set quantity to 0" is the server's delete. The UI must therefore call DELETE
 * /items/{productId} explicitly — sending 0 would work, but only by relying on
 * that removal side effect, which is not obvious at the call site.
 */
export const MIN_ITEM_QUANTITY = 1;

/** Reject a non-positive quantity before it reaches the server. */
export function isValidQuantity(quantity: number): boolean {
  return Number.isInteger(quantity) && quantity >= MIN_ITEM_QUANTITY;
}

/**
 * True when the basket holds nothing actionable.
 *
 * Guards a zero-quantity row the UI must not render as a line item. The server
 * removes such rows, but a cached or partially-applied response could still
 * carry one, and a "0 × Walnut Desk" line is worse than no line.
 */
export function isEmptyBasket(basket: Pick<Basket, 'items'>): boolean {
  return basket.items.length === 0;
}

/** Line count by distinct product, not by summed quantity. */
export function distinctItemCount(basket: Pick<Basket, 'items'>): number {
  return basket.items.filter((item) => item.quantity > 0).length;
}

/** Guard for checkout: there must be something to order. */
export function canCheckout(basket: Pick<Basket, 'items'>): boolean {
  return basket.items.some((item) => item.quantity > 0);
}