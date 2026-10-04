import { describe, expect, it } from 'vitest';

import { ApiError } from '@/lib/api/client.server';
import { actionErrorMessage } from '@/lib/api/action-error';

/**
 * The single user-facing language for Server Action failures. Pins the
 * status mapping so a future edit cannot silently re-leak transport detail
 * ("API PUT … failed with 500") into the UI.
 */
describe('actionErrorMessage', () => {
  it('maps 401 to a sign-in prompt, never a raw 401', () => {
    const message = actionErrorMessage(new ApiError('API x failed with 401', 401, 'cid-1'));
    expect(message).toMatch(/sign-in expired/i);
    expect(message).not.toMatch(/401/);
  });

  it('maps 403/404 to the ownership boundary without distinguishing them', () => {
    expect(
      actionErrorMessage(new ApiError('API x failed with 403', 403, 'cid-1')),
    ).toBe(actionErrorMessage(new ApiError('API x failed with 404', 404, 'cid-2')));
  });

  it('maps 429 to a wait-and-retry prompt', () => {
    expect(
      actionErrorMessage(new ApiError('API x failed with 429', 429, 'cid-1')),
    ).toMatch(/too many requests/i);
  });

  it('lets an action override one status without re-implementing the switch', () => {
    expect(
      actionErrorMessage(new ApiError('API x failed with 409', 409, 'cid-1'), {
        409: 'A price changed.',
      }),
    ).toBe('A price changed.');
  });

  it('keeps the default 409 for statuses without an override', () => {
    expect(actionErrorMessage(new ApiError('API x failed with 409', 409, 'cid-1'))).toMatch(
      /conflict/i,
    );
  });

  it('attaches the correlation id to unmapped statuses for support', () => {
    expect(actionErrorMessage(new ApiError('API x failed with 500', 500, 'cid-9'))).toContain(
      'cid-9',
    );
  });

  it('passes non-Api errors through untouched', () => {
    expect(actionErrorMessage(new Error('fetch failed'))).toBe('fetch failed');
    expect(actionErrorMessage('string thrown')).toBe('Something went wrong.');
  });
});
