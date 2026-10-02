import { describe, expect, it } from 'vitest';

import { describeStatus, type StatusTone } from '@/lib/ui/status';

/**
 * Status presentation.
 *
 * Two rules this module exists to enforce:
 *  1. Colour is never the only signal. Every state carries a distinct glyph
 *     shape AND a text label, so the state survives greyscale, colour-blindness
 *     and screen readers.
 *  2. An unknown status must never be rendered as a healthy one. Defaulting an
 *     unrecognised value to "good" would tell a customer their order succeeded
 *     when the saga is actually parked awaiting ManualInterventionRequired.
 */

describe('describeStatus', () => {
  it('renders Success as a settled good state', () => {
    const s = describeStatus('Success');
    expect(s.tone).toBe<StatusTone>('good');
    // Concrete copy, not a bare "Complete" — it says what actually happened.
    expect(s.label).toBe('Order confirmed');
  });

  it('renders Error as an alert with a distinct glyph', () => {
    const s = describeStatus('Error');
    expect(s.tone).toBe<StatusTone>('alert');
    expect(s.glyph).not.toBe(describeStatus('Success').glyph);
  });

  it('renders in-flight states as live, not as settled', () => {
    for (const status of ['StockSecured', 'ProcessingPayment']) {
      const s = describeStatus(status as never);
      expect(s.tone).toBe<StatusTone>('live');
      expect(s.label).not.toBe('Complete');
    }
  });

  it('renders ManualInterventionRequired as needing a human, NOT as failure', () => {
    const s = describeStatus('ManualInterventionRequired');
    expect(s.tone).toBe<StatusTone>('alert');
    // The distinction matters: "failed" invites a retry, "we're on it" does not.
    expect(s.label).not.toBe(describeStatus('Error').label);
    expect(s.label.toLowerCase()).toContain('team');
  });

  it('NEVER renders an unknown status as good or complete', () => {
    const s = describeStatus('Mystery(99)');
    expect(s.tone).not.toBe<StatusTone>('good');
    expect(s.label).not.toBe('Order confirmed');
  });

  it('always produces a non-empty label for screen readers', () => {
    for (const status of [
      'Success',
      'Error',
      'StockSecured',
      'ProcessingPayment',
      'ManualInterventionRequired',
      'SomethingBrandNew',
    ]) {
      const s = describeStatus(status as never);
      expect(s.label.trim().length).toBeGreaterThan(0);
      // The label must not just echo a raw enum name — it is prose.
      expect(s.label).not.toBe(status);
    }
  });

  it('gives every state a glyph so the UI is legible without colour', () => {
    const glyphs = (
      ['Success', 'Error', 'StockSecured', 'ManualInterventionRequired'] as const
    ).map((s) => describeStatus(s).glyph);
    expect(new Set(glyphs).size).toBeGreaterThanOrEqual(3);
  });
});