import { describe, expect, it } from 'vitest';

import {
  isKnownRealtimeStatus,
  isTerminalRealtimeStatus,
  mapRealtimeStatus,
  needsManualIntervention,
  ORDER_STATUS_CHANGED_TYPE,
  parseCloudEvent,
  parseCoalescedBatch,
  REALTIME_STATUS,
} from '@/lib/real-time/messages';

/**
 * Wire format captured from the real Wolverine CloudEvents envelope.
 * The lowercase envelope keys (specversion/datacontenttype/traceparent/tenantid)
 * are NOT camelCase — they come from JsonPropertyName attributes on
 * Wolverine.Runtime.Interop.CloudEventsEnvelope.
 */
const REAL_FRAME = JSON.stringify({
  topic: null,
  tenantid: null,
  traceid: null,
  tracestate: null,
  data: { orderId: '9f1c...', status: 'Success', message: 'Your order has been confirmed!' },
  id: '08df209f-1111-2222-3333-444455556666',
  specversion: '1.0',
  datacontenttype: 'application/json; charset=utf-8',
  source: null,
  type: 'order_status_changed',
  time: '2026-10-02T16:11:25.0113761+00:00',
  traceparent: null,
});

describe('realtime status vocabulary', () => {
  it('covers every status the saga actually emits', () => {
    // Verified at each `new OrderStatusChanged(...)` call site:
    // HappyPath.cs:40 StockSecured, Timeouts.cs:57 ProcessingPayment,
    // Timeouts.cs:250 ManualInterventionRequired, plus Success and Error.
    for (const status of Object.values(REALTIME_STATUS)) {
      expect(isKnownRealtimeStatus(status)).toBe(true);
    }
  });

  it('includes ManualInterventionRequired, which MESSAGING_PATTERNS.md omits', () => {
    expect(isKnownRealtimeStatus('ManualInterventionRequired')).toBe(true);
  });

  it('does NOT accept persisted OrderStatus enum names', () => {
    // StockConfirmed / AwaitingValidation are the REST enum. Mixing the two
    // vocabularies silently mislabels every order.
    expect(isKnownRealtimeStatus('StockConfirmed')).toBe(false);
    expect(isKnownRealtimeStatus('AwaitingValidation')).toBe(false);
    expect(isKnownRealtimeStatus('Submitted')).toBe(false);
  });

  it('surfaces an unknown status verbatim instead of guessing', () => {
    expect(mapRealtimeStatus('SomethingBrandNew')).toBe('Unknown(SomethingBrandNew)');
  });

  it('does not default an unknown status to Error', () => {
    // Defaulting would tell a customer their order failed when a human
    // actually needs to intervene.
    expect(mapRealtimeStatus('ManualInterventionRequired')).not.toBe(REALTIME_STATUS.Error);
    expect(mapRealtimeStatus('Mystery')).not.toBe(REALTIME_STATUS.Error);
  });

  it('treats Success and Error as terminal', () => {
    expect(isTerminalRealtimeStatus('Success')).toBe(true);
    expect(isTerminalRealtimeStatus('Error')).toBe(true);
    expect(isTerminalRealtimeStatus('ProcessingPayment')).toBe(false);
    expect(isTerminalRealtimeStatus('ManualInterventionRequired')).toBe(false);
  });

  it('flags manual intervention distinctly from failure', () => {
    expect(needsManualIntervention('ManualInterventionRequired')).toBe(true);
    expect(needsManualIntervention('Error')).toBe(false);
  });
});

describe('parseCloudEvent', () => {
  it('parses the real Wolverine frame', () => {
    const event = parseCloudEvent(REAL_FRAME);
    expect(event).not.toBeNull();
    expect(event!.type).toBe(ORDER_STATUS_CHANGED_TYPE);
    expect(event!.specversion).toBe('1.0');
    expect(event!.data.status).toBe('Success');
    expect(event!.data.message).toBe('Your order has been confirmed!');
  });

  it('exposes camelCase fields inside data despite lowercase envelope keys', () => {
    const event = parseCloudEvent(REAL_FRAME)!;
    expect(event.data.orderId).toBe('9f1c...');
  });

  it('returns null for a malformed frame rather than throwing in a handler', () => {
    expect(parseCloudEvent('not json')).toBeNull();
    expect(parseCloudEvent('{}')).toBeNull();
    expect(parseCloudEvent('null')).toBeNull();
  });

  it('returns null for an envelope with no type', () => {
    expect(parseCloudEvent(JSON.stringify({ data: { orderId: 'x' } }))).toBeNull();
  });
});

describe('parseCoalescedBatch', () => {
  it('parses a batch whose items are STRINGS holding whole CloudEvents', () => {
    const batch = JSON.stringify({ wolverineBatch: true, items: [REAL_FRAME] });
    const events = parseCoalescedBatch(batch);
    expect(events).toHaveLength(1);
    expect(events[0]!.data.status).toBe('Success');
  });

  it('handles several items', () => {
    const other = REAL_FRAME.replace('"status":"Success"', '"status":"ProcessingPayment"');
    const batch = JSON.stringify({ wolverineBatch: true, items: [REAL_FRAME, other] });
    expect(parseCoalescedBatch(batch)).toHaveLength(2);
  });

  it('drops malformed items without discarding the good ones', () => {
    const batch = JSON.stringify({ wolverineBatch: true, items: ['garbage', REAL_FRAME] });
    expect(parseCoalescedBatch(batch)).toHaveLength(1);
  });

  it('returns an empty array for a non-batch frame', () => {
    expect(parseCoalescedBatch(REAL_FRAME)).toEqual([]);
    expect(parseCoalescedBatch('not json')).toEqual([]);
  });
});