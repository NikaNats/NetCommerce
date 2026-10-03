'use server';

import { revalidatePath } from 'next/cache';

import { addBasketItem } from '@/lib/api/catalog.server';
import { newIdempotencyKey } from '@/lib/api/headers';
import { isValidQuantity } from '@/lib/basket/basket';
import { requireSession } from '@/lib/auth/guards';

/**
 * Add a product to the basket.
 *
 * ## Why this is a Server Action and not a client fetch
 *
 * The basket endpoints are `.RequireAuthorization()` and rate limited per user.
 * A client-side fetch would need a bearer token in the browser, which is exactly
 * what this architecture refuses to have. Going through an action keeps the token
 * in the server session.
 *
 * ## The price sent here is a DISPLAY echo, not an authority
 *
 * AddBasketItemRequest carries UnitPrice because the server's DTO requires it,
 * but ShoppingBasket stores whatever it is given — the server does NOT re-price
 * the line. So a tampered value would be stored and shown back as the basket
 * total. That is a known gap in the API (BasketEndpoints.cs:80-88), deliberately
 * left unpatched here; checkout resolves real prices server-side, so the
 * authority at payment time is unaffected. Fixing it belongs in the API, not in
 * a frontend that could only hide the problem.
 */
export interface AddToCartState {
  ok: boolean;
  message: string;
  /** Echoed so the client can show WHICH item failed. */
  productName?: string;
}

export async function addToCartAction(
  _prev: AddToCartState,
  formData: FormData,
): Promise<AddToCartState> {
  const productId = String(formData.get('productId') ?? '');
  const productName = String(formData.get('productName') ?? '');
  // Empty form fields normalize to undefined so the key is omitted from the
  // JSON body. The API's AddBasketItemRequest takes nullable strings, so an
  // explicit null would bind identically — omission is just the cleaner
  // encoding of "no value" and keeps sku/imageUrl as truly-optional keys.
  const sku = String(formData.get('sku') ?? '') || undefined;
  const unitPriceRaw = String(formData.get('unitPrice') ?? '');
  const quantityRaw = String(formData.get('quantity') ?? '1');
  const imageUrl = String(formData.get('imageUrl') ?? '') || undefined;

  // A signed-out visitor is redirected to login and returns here afterwards.
  await requireSession(`/products/${productId}`);

  if (!productId) {
    return { ok: false, message: 'Missing product identifier.' };
  }

  const quantity = Number(quantityRaw);
  if (!isValidQuantity(quantity)) {
    return { ok: false, message: 'Choose a quantity of at least 1.', productName };
  }

  const unitPrice = Number(unitPriceRaw);
  if (!Number.isFinite(unitPrice) || unitPrice < 0) {
    return { ok: false, message: 'That product has no usable price.', productName };
  }

  try {
    await addBasketItem(
      { productId, productName, sku, quantity, unitPrice, imageUrl },
      // One key per submission: a retry of THIS action must not double the
      // quantity, because the server ADDS to the existing line rather than
      // replacing it.
      newIdempotencyKey(),
    );
  } catch (cause) {
    const message =
      cause instanceof Error ? cause.message : 'Could not reach the API.';
    return { ok: false, message, productName };
  }

  // The basket is per-user server state; the product tile is not affected.
  revalidatePath('/basket');

  return { ok: true, message: `${productName} added to your basket.`, productName };
}