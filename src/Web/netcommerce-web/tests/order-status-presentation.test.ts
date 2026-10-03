import { describe, expect, it } from 'vitest';

import { mapOrderStatus } from '@/lib/orders/order-status';
import { describeStatus } from '@/lib/ui/status';
import { REALTIME_STATUS } from '@/lib/real-time/messages';

/**
 * Both status vocabularies must be presentable.
 *
 * `OrderSagaState.status` holds either the persisted enum (written by REST
 * reconciliation) or the saga's free-form strings (written by the socket). They land
 * in ONE field and go through ONE presentation function.
 *
 * `PRESENTATION` in status.ts originally listed only the five realtime strings, so
 * every authoritative status rendered through the `UNKNOWN` fallback — "Status
 * unavailable — refreshing". The visible effect was a warning banner on a
 * successfully PAID and DELIVERED order, which is worse than showing nothing.
 *
 * These tests pin both halves: the REST vocabulary must have real copy, and the
 * UNKNOWN fallback must remain reserved for genuinely unrecognised values.
 */

/** Every member of the persisted OrderStatus enum. Keep in sync with Order.cs. */
const REST_STATUSES = [
  'Submitted',
  'AwaitingValidation',
  'StockConfirmed',
  'Paid',
  'Shipped',
  'Delivered',
  'Cancelled',
] as const;

const UNKNOWN_LABEL = 'Status unavailable — refreshing';

describe('the persisted status vocabulary is presentable', () => {
  it.each(REST_STATUSES)('%s does NOT fall through to the unknown banner', (name) => {
    // The defect, stated as a test: any of these hitting UNKNOWN meant a customer
    // saw a warning for a normal order state.
    expect(describeStatus(name).label).not.toBe(UNKNOWN_LABEL);
  });

  it.each(REST_STATUSES)('%s has a distinct label', (name) => {
    const label = describeStatus(name).label;
    expect(label.length).toBeGreaterThan(0);
    expect(label).not.toBe(UNKNOWN_LABEL);
  });

  it('a completed order reads as good news, not a warning', () => {
    // The two a customer most wants confirmed.
    expect(describeStatus('Paid').tone).toBe('live');
    expect(describeStatus('Delivered').tone).toBe('good');
  });

  it('a cancelled order is presented as a problem', () => {
    expect(describeStatus('Cancelled').tone).toBe('alert');
  });
});

describe('the realtime vocabulary is still presentable', () => {
  it.each(Object.values(REALTIME_STATUS))(
    '%s does NOT fall through to the unknown banner',
    (name) => {
      // Guards the half that already worked, so adding the REST entries cannot have
      // displaced them.
      expect(describeStatus(name).label).not.toBe(UNKNOWN_LABEL);
    },
  );
});

describe('the two vocabularies do not collide', () => {
  it('a REST name is not a realtime status', () => {
    // The invariant that motivated the separate mappers.
    const realtime = Object.values(REALTIME_STATUS);
    for (const name of REST_STATUSES) {
      expect(realtime).not.toContain(name);
    }
  });

  it('REST names and their realtime counterparts may share shopper-facing copy', () => {
    // StockConfirmed (persisted) and StockSecured (push) are different STRINGS that
    // mean the same thing to a shopper, so they are DELIBERATELY given identical
    // copy. That is correct: a customer should not see the vocabulary change when
    // the page switches source mid-session.
    //
    // What must differ is the underlying value — asserted by the test above. This
    // pins the intent so a future "fix" does not make one of them read as unknown
    // just because it no longer matches the other.
    expect(describeStatus('StockConfirmed').label).toBe(
      describeStatus('StockSecured').label,
    );
    expect(describeStatus('StockConfirmed').tone).toBe(
      describeStatus('StockSecured').tone,
    );
  });
});

describe('the unknown fallback is still reserved for real unknowns', () => {
  it.each([
    ['a raw realtime unknown', 'Unknown(Nonsense)'],
    ['a raw REST unknown', 'Unknown(99)'],
    ['an arbitrary string', 'whatever'],
    ['an empty string', ''],
  ])('%s falls through honestly', (_label, value) => {
    // The fallback must NOT be loosened to make the tests above pass. An
    // unrecognised value must stay visible as unknown rather than being coerced
    // into a confident-sounding state.
    expect(describeStatus(value).label).toBe(UNKNOWN_LABEL);
  });
});

describe('mapOrderStatus feeds the presentation layer', () => {
  it('maps every enum code to a presentable name', () => {
    // 0..6 are Submitted..Cancelled in Order.cs. Round-tripping proves the two
    // tables agree, which is what makes the page render correctly at all.
    const codes = [0, 1, 2, 3, 4, 5, 6] as const;

    for (const code of codes) {
      const name = mapOrderStatus(code);
      expect(describeStatus(name).label).not.toBe(UNKNOWN_LABEL);
    }
  });

  it('an out-of-range code stays visible as unknown', () => {
    expect(mapOrderStatus(99)).toBe('Unknown(99)');
    expect(describeStatus(mapOrderStatus(99)).label).toBe(UNKNOWN_LABEL);
  });
});