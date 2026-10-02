/**
 * Order status vocabulary, mirroring the server's enum exactly.
 *
 * Source of truth: src/Ordering/Ordering.Domain/Orders/Order.cs:302-334
 *
 *   Submitted = 0          order placed, stock soft-reserved, payment NOT taken
 *   AwaitingValidation = 1  grace period over, ready for payment capture
 *   StockConfirmed = 2     stock confirmed for the order
 *   Paid = 3               payment received
 *   Shipped = 4            order shipped
 *   Delivered = 5          order delivered
 *   Cancelled = 6          order cancelled
 *
 * Do NOT invent members here. The previous design guide mapped 1 to
 * "ProcessingPayment" and 2 to "StockSecured" — neither exists server-side, so
 * every order in those states was mislabelled in the UI.
 */
export const ORDER_STATUS = {
  Submitted: 0,
  AwaitingValidation: 1,
  StockConfirmed: 2,
  Paid: 3,
  Shipped: 4,
  Delivered: 5,
  Cancelled: 6,
} as const;

export type KnownOrderStatusName = keyof typeof ORDER_STATUS;

/**
 * A status name as surfaced to the UI.
 *
 * This is deliberately wider than the known set. An unrecognized server value is
 * reported as `Unknown(<raw>)` rather than being coerced into a wrong state, so
 * the type has to admit those values — otherwise the honest path needs a cast,
 * and the cast is where a mislabelled order would slip through.
 */
export type OrderStatusName = KnownOrderStatusName | `Unknown(${string})`;

const NAME_BY_CODE: ReadonlyMap<number, KnownOrderStatusName> = new Map(
  Object.entries(ORDER_STATUS).map(([name, code]) => [code, name as KnownOrderStatusName]),
);

const NAME_SET: ReadonlySet<string> = new Set(Object.keys(ORDER_STATUS));

/** Narrow an arbitrary string to a status the server actually defines. */
export function isKnownStatus(value: string): value is KnownOrderStatusName {
  return NAME_SET.has(value);
}

/** Statuses after which no further server-side transition is expected. */
export const TERMINAL_STATUSES: ReadonlySet<KnownOrderStatusName> =
  new Set<KnownOrderStatusName>(['Delivered', 'Cancelled']);

/**
 * True while the customer may still cancel without being charged.
 * Submitted is the only state inside the cooling-off window.
 */
export function isCancellable(status: OrderStatusName): boolean {
  return status === 'Submitted';
}

/**
 * Normalize a status from the API into a name the UI can render.
 *
 * The API serializes the enum numerically today, but accepts/parses the member
 * name in some projections, so both shapes are handled. An unrecognized value
 * is surfaced verbatim rather than silently coerced into a wrong state — a
 * mislabelled order is worse than an honest "we don't know this yet".
 */
export function mapOrderStatus(raw: number | string): OrderStatusName {
  if (typeof raw === 'number') {
    return NAME_BY_CODE.get(raw) ?? `Unknown(${raw})`;
  }
  return isKnownStatus(raw) ? raw : `Unknown(${raw})`;
}
