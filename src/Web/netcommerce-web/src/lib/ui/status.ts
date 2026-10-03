/**
 * Status presentation.
 *
 * Two rules this module exists to enforce:
 *
 *  1. Colour is never the only signal. Each state carries a distinct glyph shape
 *     and a prose label, so state survives greyscale, colour-blindness and
 *     screen readers.
 *  2. An unknown status must never be rendered as a healthy one. Defaulting an
 *     unrecognised value to "good" would tell a customer their order succeeded
 *     when the saga is actually parked awaiting ManualInterventionRequired.
 *
 * The vocabulary is the REALTIME one from lib/real-time/messages.ts, which is
 * not the persisted OrderStatus enum and not the (incomplete) list in
 * docs/MESSAGING_PATTERNS.md.
 */

export type StatusTone = 'live' | 'good' | 'alert';

export interface StatusPresentation {
  tone: StatusTone;
  /** Prose for humans and screen readers. Never a raw enum name. */
  label: string;
  /** Glyph key; the component maps this to an SVG shape. */
  glyph: 'dot' | 'ring' | 'triangle' | 'bar';
}

/**
 * Manual intervention is presented distinctly from failure on purpose. Telling a
 * customer an order "failed" invites a retry; telling them the team is on it
 * does not, and it matches what the saga is actually doing — parked, not dead.
 */
const PRESENTATION: Record<string, StatusPresentation> = {
  // ---- Vocabulary 2: the saga's realtime push strings ----
  Success: { tone: 'good', label: 'Order confirmed', glyph: 'dot' },
  Error: { tone: 'alert', label: 'Could not be completed', glyph: 'triangle' },
  ManualInterventionRequired: {
    tone: 'alert',
    label: 'Our team is reviewing this order',
    glyph: 'triangle',
  },
  StockSecured: { tone: 'live', label: 'Stock reserved', glyph: 'ring' },
  ProcessingPayment: { tone: 'live', label: 'Taking payment', glyph: 'ring' },

  // ---- Vocabulary 1: the persisted OrderStatus enum ----
  //
  // These were MISSING, so every authoritative status rendered as
  // "Status unavailable — refreshing" — including `Paid` and `Delivered`, the two
  // a customer most wants confirmed. The page would show a warning banner for a
  // successfully completed order.
  //
  // REST reconciliation writes this vocabulary and the socket writes the other one
  // into the SAME state field, so both must be presentable here. The two sets are
  // kept visibly separate because they are genuinely different vocabularies — see
  // src/lib/real-time/messages.ts.
  Submitted: { tone: 'live', label: 'Order received', glyph: 'dot' },
  AwaitingValidation: { tone: 'live', label: 'Checking your order', glyph: 'ring' },
  StockConfirmed: { tone: 'live', label: 'Stock reserved', glyph: 'ring' },
  Paid: { tone: 'live', label: 'Payment received', glyph: 'ring' },
  Shipped: { tone: 'live', label: 'On its way', glyph: 'ring' },
  Delivered: { tone: 'good', label: 'Delivered', glyph: 'dot' },
  Cancelled: { tone: 'alert', label: 'Order cancelled', glyph: 'triangle' },
};

const UNKNOWN: StatusPresentation = {
  tone: 'live',
  label: 'Status unavailable — refreshing',
  glyph: 'bar',
};

export function describeStatus(status: string): StatusPresentation {
  return PRESENTATION[status] ?? UNKNOWN;
}