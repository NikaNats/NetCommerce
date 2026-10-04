'use server';

import { revalidatePath } from 'next/cache';

import {
  clearBasket,
  removeBasketItem,
  updateBasketItemQuantity,
} from '@/lib/api/catalog.server';
import { actionErrorMessage } from '@/lib/api/action-error';
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
    return { ok: false, message: actionErrorMessage(cause) };
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
    return { ok: false, message: actionErrorMessage(cause) };
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
    await clearBasket(newIdempotencyKey());
  } catch (cause) {
    return { ok: false, message: actionErrorMessage(cause) };
  }

  revalidatePath('/basket');
  return { ok: true, message: 'Basket cleared.' };
}