import { ApiError } from '@/lib/api/client.server';

/**
 * One user-facing language for Server Action failures.
 *
 * Every action used to map ApiError statuses to messages inline — the same
 * 401/429 branches copied across checkout, cancel and basket actions, drifting
 * wording and leaking transport detail ("API PUT … failed with 500") wherever
 * a branch was forgotten. Status-to-message knowledge lives here now, once.
 *
 * Special-general split: the defaults cover the common statuses; an action
 * with a context-specific message for one status (the checkout price-conflict,
 * the cancel cooling-off window) passes it as an override instead of
 * re-implementing the whole switch. Behavior beyond the message (revalidating
 * the basket on a price conflict) stays in the action — this module only
 * words failures, never handles them.
 */
export function actionErrorMessage(
  cause: unknown,
  overrides: Record<number, string> = {},
): string {
  if (cause instanceof ApiError) {
    const override = overrides[cause.status];
    if (override !== undefined) return override;

    switch (cause.status) {
      case 401:
        return 'Your sign-in expired. Sign in again to continue.';
      case 403:
      case 404:
        return 'This does not exist, or it belongs to another account.';
      case 409:
        return 'This conflicts with a newer change. Refresh and try again.';
      case 429:
        return 'Too many requests — wait a moment and try again.';
      default:
        return `Something went wrong. Reference ${cause.correlationId}.`;
    }
  }

  return cause instanceof Error ? cause.message : 'Something went wrong.';
}
