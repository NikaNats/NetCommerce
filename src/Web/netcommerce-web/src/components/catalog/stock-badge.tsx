import { stockState, type StockDto } from '@/lib/inventory/stock';

/**
 * Availability badge for a product detail page.
 *
 * Presentational only: the caller fetches the record (getStockByProductId)
 * and owns the buy gate — this component turns one StockState into words.
 * `unknown` renders nothing rather than a hedge ("availability unknown" next
 * to a buy button helps nobody); `out-of-stock` explains why no form follows.
 */
export function StockBadge({ stock }: { stock: StockDto | null }) {
  switch (stockState(stock)) {
    case 'unknown':
      return null;
    case 'in-stock':
      return (
        <p className="field-label" role="status" style={{ margin: 0 }}>
          In stock
        </p>
      );
    case 'low-stock':
      return (
        <p className="field-label" role="status" style={{ margin: 0 }}>
          Low stock
          {stock && stock.availableQuantity > 0 ? (
            <>
              {' '}
              — <span className="figure">only {stock.availableQuantity} left</span>
            </>
          ) : null}
        </p>
      );
    case 'out-of-stock':
      return (
        <p className="notice" role="status" style={{ margin: 0 }}>
          <strong>Out of stock</strong>
          <span style={{ display: 'block' }}>
            There is nothing available to reserve right now, so this item cannot
            be ordered.
          </span>
        </p>
      );
  }
}
