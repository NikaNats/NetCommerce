/**
 * Money formatting for display only.
 *
 * ## Never use this for arithmetic
 *
 * The server sends `decimal` and computes `Basket.totalPrice` itself. Formatting
 * here must never feed a value back into a request: `Number(x)` on a formatted
 * string is how a "1299.00" becomes 1299 and a summed total drifts from
 * checkout by cents. Format at the edge, keep numbers as numbers.
 */

/** Currencies the API can emit, per PriceBreakdown/Money on the server. */
const ZERO_DECIMAL = new Set(['JPY', 'KRW', 'VND', 'CLP', 'ISK']);

/**
 * Format an amount with its currency.
 *
 * Falls back to a plain 2-decimal rendering for an unknown or missing currency
 * rather than throwing: a malformed price must not blank the whole catalog tile.
 * A missing currency is shown as an explicit marker rather than a bare number,
 * because "1299.00" with no currency reads as a claim about a price.
 */
export function formatMoney(amount: number, currency?: string | null): string {
  if (!Number.isFinite(amount)) {
    return '—';
  }

  if (!currency) {
    return `${amount.toFixed(2)} ??`;
  }

  const code = currency.toUpperCase();
  const digits = ZERO_DECIMAL.has(code) ? 0 : 2;

  try {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: code,
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(amount);
  } catch {
    // Intl throws RangeError on a malformed ISO code.
    return `${amount.toFixed(2)} ${code}`;
  }
}

/**
 * Format a possibly-absent money value for a table cell.
 *
 * Distinguishes "absent" from "zero": a blank cell and a 0.00 cell mean different
 * things in a financial table, and collapsing them loses information.
 */
export function formatMoneyOrDash(
  amount: number | null | undefined,
  currency?: string | null,
): string {
  if (amount === null || amount === undefined) return '—';
  return formatMoney(amount, currency);
}