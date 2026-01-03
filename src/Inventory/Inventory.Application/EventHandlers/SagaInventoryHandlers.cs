using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Events;
using Wolverine.Attributes;

namespace NetCommerce.Inventory.Application.EventHandlers;

// ============================================================================
// DEPRECATED: These handlers have been superseded by PartitionedStockHandlers
// in the Infrastructure layer.
//
// The new handlers use Partitioned Sequential Messaging for high-contention
// scenarios. All inventory commands (ReserveInventoryCommand, ConfirmInventoryCommand,
// ReleaseInventoryReservationCommand) are now processed in the "inventory-contention"
// local queue with message partitioning by ProductId.
//
// This ensures:
// - No database locks (FOR UPDATE) needed
// - Thread-safe by design (same ProductId = same thread)
// - 11 parallel tracks for different products
// - Zero DB deadlocks
//
// See: src/Inventory/Inventory.Infrastructure/Handlers/PartitionedStockHandlers.cs
// ============================================================================

// NOTE: The handlers below are kept for reference but are disabled.
// Wolverine will use the handlers in PartitionedStockHandlers.cs instead
// because they are configured with [LocalQueue("inventory-contention")].

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

/*
/// <summary>
///     DEPRECATED: Replaced by PartitionedReserveInventoryHandler.
///     Handlers for Saga commands in the Inventory module.
///     These handlers process inventory operations from the OrderFulfillmentSaga.
/// </summary>
[WolverineHandler]
public static class SagaInventoryHandlers
{
    // ... Original implementation removed in favor of partitioned handlers
}
*/

#pragma warning restore CS1591
