'use server';

import { revalidatePath } from 'next/cache';

import { apiFetch } from '@/lib/api/client.server';
import {
  removeBasketItem,
  updateBasketItemQuantity,
} from '@/lib/api/catalog.server';
import { newIdempotencyKey } from '@/lib/api/headers';
import { isValidQuantity } from '@/lib/basket/basket';
import { requireSession } from '@/lib/auth/guards';

export interface BasketActionState {
  ok: boolean;
  message: string;
}

/** PUT /api/v1/basket/items/{productId} */
export async function setQuantityAction(
  _prev: BasketActionState,
  formData: FormData,
): Promise<BasketActionState> {
  await requireSession('/basket');

  const productId = String(formData.get('productId') ?? '');
  const quantity = Number(formData.get('quantity'));

  if (!productId) {
    return { ok: false, message: 'Missing product identifier.' };
  }

  // quantity <= 0 removes the row server-side, but that is a hidden side effect
  // of UpdateItemQuantity. Delete is the explicit route for removal, so a
  // non-positive quantity here is rejected rather than silently deleting.
  if (!isValidQuantity(quantity)) {
    return { ok: false, message: 'Quantity must be at least 1.' };
  }

  try {
    await updateBasketItemQuantity(productId, quantity, newIdempotencyKey());
  } catch (cause) {
    return {
      ok: false,
      message: cause instanceof Error ? cause.message : 'Could not update the basket.',
    };
  }

  revalidatePath('/basket');
  return { ok: true, message: 'Quantity updated.' };
}

/** DELETE /api/v1/basket/items/{productId} */
export async function removeItemAction(
  _prev: BasketActionState,
  formData: FormData,
): Promise<BasketActionState> {
  await requireSession('/basket');

  const productId = String(formData.get('productId') ?? '');
  if (!productId) {
    return { ok: false, message: 'Missing product identifier.' };
  }

  try {
    await removeBasketItem(productId, newIdempotencyKey());
  } catch (cause) {
    return {
      ok: false,
      message: cause instanceof Error ? cause.message : 'Could not remove that item.',
    };
  }

  revalidatePath('/basket');
  return { ok: true, message: 'Item removed.' };
}

/** DELETE /api/v1/basket */
export async function clearBasketAction(
  _prev: BasketActionState,
  _formData: FormData,
): Promise<BasketActionState> {
  await requireSession('/basket');

  try {
    // DELETE /api/v1/basket — no catalog.server wrapper exists for the
    // clear-basket route, so this calls the client directly rather than
    // reaching into the catalog module for it.
    await apiFetch<void>('/api/v1/basket', { method: 'DELETE', cache: 'no-store' });
  } catch (cause) {
    return {
      ok: false,
      message: cause instanceof Error ? cause.message : 'Could not clear the basket.',
    };
  }

  revalidatePath('/basket');
  return { ok: true, message: 'Basket cleared.' };
}