import { describe, expect, it } from 'vitest';

import {
  isCancellable,
  mapOrderStatus,
  TERMINAL_STATUSES,
  type OrderStatusName,
} from '@/lib/orders/order-status';

/**
 * Mirrors src/Ordering/Ordering.Domain/Orders/Order.cs:302-334 exactly.
 *   Submitted = 0, AwaitingValidation = 1, StockConfirmed = 2, Paid = 3,
 *   Shipped = 4, Delivered = 5, Cancelled = 6
 */
describe('mapOrderStatus', () => {
  it('maps Submitted (0)', () => {
    expect(mapOrderStatus(0)).toBe<OrderStatusName>('Submitted');
  });

  it('maps AwaitingValidation (1) — the grace period has expired', () => {
    expect(mapOrderStatus(1)).toBe<OrderStatusName>('AwaitingValidation');
  });

  it('maps StockConfirmed (2)', () => {
    expect(mapOrderStatus(2)).toBe<OrderStatusName>('StockConfirmed');
  });

  it('maps Paid (3)', () => {
    expect(mapOrderStatus(3)).toBe<OrderStatusName>('Paid');
  });

  it('maps Shipped (4)', () => {
    expect(mapOrderStatus(4)).toBe<OrderStatusName>('Shipped');
  });

  it('maps Delivered (5)', () => {
    expect(mapOrderStatus(5)).toBe<OrderStatusName>('Delivered');
  });

  it('maps Cancelled (6)', () => {
    expect(mapOrderStatus(6)).toBe<OrderStatusName>('Cancelled');
  });

  it('accepts the enum member name verbatim (REST may serialize as string)', () => {
    expect(mapOrderStatus('StockConfirmed')).toBe<OrderStatusName>('StockConfirmed');
    expect(mapOrderStatus('AwaitingValidation')).toBe<OrderStatusName>('AwaitingValidation');
  });

  it('falls back to the raw value for an unknown status instead of guessing', () => {
    expect(mapOrderStatus(99)).toBe<OrderStatusName>('Unknown(99)');
    expect(mapOrderStatus('SomethingNew')).toBe<OrderStatusName>('Unknown(SomethingNew)');
  });

  it('marks Cancelled and Delivered as terminal, Paid as not', () => {
    expect(TERMINAL_STATUSES.has('Cancelled')).toBe(true);
    expect(TERMINAL_STATUSES.has('Delivered')).toBe(true);
    expect(TERMINAL_STATUSES.has('Paid')).toBe(false);
  });

  it('is not cancellable outside Submitted', () => {
    expect(isCancellable('Submitted')).toBe(true);
    expect(isCancellable('AwaitingValidation')).toBe(false);
    expect(isCancellable('Paid')).toBe(false);
  });

  it('treats an unknown status as not cancellable', () => {
    expect(isCancellable('Unknown(99)')).toBe(false);
  });
});
