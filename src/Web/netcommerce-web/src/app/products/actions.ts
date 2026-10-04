'use server';

import { revalidatePath } from 'next/cache';

import { addBasketItem } from '@/lib/api/catalog.server';
import { actionErrorMessage } from '@/lib/api/action-error';
import { newIdempotencyKey } from '@/lib/api/headers';
import { isValidQuantity } from '@/lib/basket/basket';
import { requireSession } from '@/lib/auth/guards';
import { isProductId, productIdPath } from '@/lib/catalog/products';

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
 * ## The price is no longer sent at all
  *
  * AddBasketItemRequest used to carry UnitPrice, and ShoppingBasket stored whatever
  * it was given — a tampered value was persisted and shown back as the basket total.
  * That was a known gap in the API (BasketEndpoints.cs), left deliberately unpatched
  * here because a frontend could only hide it.
  *
  * It is now fixed at the SOURCE: the endpoint resolves price, name, SKU and image
  * from the catalog, and the request contract carries only productId and quantity.
  * So this action no longer reads a price from the form at all. There is nothing
  * left here for a tampered hidden field to poison — the field no longer exists on
  * either side of the wire.
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
    const quantityRaw = String(formData.get('quantity') ?? '1');

    // A signed-out visitor is redirected to login and returns here afterwards.
    //
    // The returnTo MUST use the /id/ segment. There are two product detail routes:
    //
    //   /products/[slug]      resolves via getProductBySlug(slug)
    //   /products/id/[id]     resolves via getProductById(id)
    //
    // `productId` is a UUID, so `/products/${productId}` matches the SLUG route and
    // the backend looks up a slug equal to a GUID — a miss, a 404, and notFound().
    // The shopper would sign in successfully and land on "Page not found", on the
    // primary conversion path.
    //
    // Validated before use: returnTo is attacker-reachable via the query string, so a
    // non-UUID would rebuild the same bug from the other direction.
    if (!isProductId(productId)) {
      return { ok: false, message: 'Missing product identifier.' };
    }
    await requireSession(productIdPath(productId));

    const quantity = Number(quantityRaw);
    if (!isValidQuantity(quantity)) {
      return { ok: false, message: 'Choose a quantity of at least 1.', productName };
    }

    try {
      await addBasketItem(
        // productId and quantity only. The server prices the line from the catalog,
        // so there is nothing to send for price — and nothing a tampered form field
        // could poison.
        { productId, quantity },
        // One key per submission: a retry of THIS action must not double the
        // quantity, because the server ADDS to the existing line rather than
        // replacing it.
        newIdempotencyKey(),
      );
    } catch (cause) {
      return { ok: false, message: actionErrorMessage(cause), productName };
    }

  // The basket is per-user server state; the product tile is not affected.
  revalidatePath('/basket');

  return { ok: true, message: `${productName} added to your basket.`, productName };
}