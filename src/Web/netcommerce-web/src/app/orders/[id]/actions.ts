'use server';

import { revalidatePath } from 'next/cache';

import { actionErrorMessage } from '@/lib/api/action-error';
import { newIdempotencyKey } from '@/lib/api/headers';
import { requireSession } from '@/lib/auth/guards';
import { cancelOrder } from '@/lib/orders/orders.server';

export interface CancelOrderState {
  ok: boolean;
  message: string;
}

/**
 * DELETE /api/v1/orders/{id} — cancels while the order is still inside the
 * cooling-off window (Submitted). Anything later is rejected by the domain
 * with a 409, which surfaces here as a message rather than a crash.
 */
export async function cancelOrderAction(
  _prev: CancelOrderState,
  formData: FormData,
): Promise<CancelOrderState> {
  const orderId = String(formData.get('orderId') ?? '');
  if (!orderId) {
    return { ok: false, message: 'Missing order identifier.' };
  }

  await requireSession(`/orders/${orderId}`);

  const reason = String(formData.get('reason') ?? '').trim() || undefined;

  try {
    const result = await cancelOrder(orderId, reason, newIdempotencyKey());
    revalidatePath(`/orders/${orderId}`);
    return { ok: true, message: result.message };
  } catch (cause) {
    return {
      ok: false,
      message: actionErrorMessage(cause, {
        // Reached only when the order moved into a terminal state between
        // render and submit — the domain rejects Delivered/Cancelled.
        409: 'This order can no longer be cancelled.',
      }),
    };
  }
}
