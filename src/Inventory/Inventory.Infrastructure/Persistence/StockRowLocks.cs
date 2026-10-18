using Microsoft.EntityFrameworkCore;

namespace NetCommerce.Inventory.Infrastructure.Persistence;

/// <summary>
///     Pessimistic row locks on <c>inventory.stocks</c>, taken in a statement of their own.
/// </summary>
/// <remarks>
///     <para>
///     Never fold <c>FOR UPDATE</c> into the query that also loads the reservations
///     (<c>FromSql("... FOR UPDATE").Include(s =&gt; s.Reservations)</c>). Under READ COMMITTED,
///     a statement that blocks on the lock re-reads only the LOCKED row once it is released;
///     every joined row still comes from the snapshot taken before it blocked. The waiter
///     therefore sees the fresh stock row (so the xmin check passes) next to a STALE
///     reservation set missing the holds its predecessor just committed, and over-allocates.
///     Measured: 40 of 40 concurrent reservations succeeded against 10 units.
///     </para>
///     <para>
///     Locking first and loading in a SEPARATE statement fixes it: READ COMMITTED takes a new
///     snapshot per statement, so the load runs after the lock is held and sees every
///     reservation committed by earlier holders. Callers must already be inside an explicit
///     transaction, or autocommit releases the lock as soon as this statement ends.
///     </para>
/// </remarks>
internal static class StockRowLocks
{
    /// <summary>
    ///     Locks the stock rows for <paramref name="productIds"/> in ascending product-id order
    ///     (a single global order, so multi-product lockers cannot deadlock each other).
    /// </summary>
    public static Task LockAsync(InventoryDbContext db, Guid[] productIds, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM inventory.stocks WHERE product_id = ANY({productIds}) ORDER BY product_id FOR UPDATE",
            ct);
}
