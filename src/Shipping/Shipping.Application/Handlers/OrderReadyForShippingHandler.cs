using Microsoft.Extensions.Logging;
using NetCommerce.Domain.Shared.Events;
using NetCommerce.Shipping.Application.Services;
using Wolverine;

namespace NetCommerce.Shipping.Application.Handlers;

/// <summary>
///     Wolverine handler for processing OrderReadyForShipping integration events.
///     This handler creates shipping labels via courier adapters and publishes
///     confirmation events back to the Ordering module.
/// </summary>
public sealed class OrderReadyForShippingHandler
{
    private readonly IShippingService _shippingService;
    private readonly ILogger<OrderReadyForShippingHandler> _logger;

    public OrderReadyForShippingHandler(
        IShippingService shippingService,
        ILogger<OrderReadyForShippingHandler> logger)
    {
        _shippingService = shippingService;
        _logger = logger;
    }

    /// <summary>
    ///     Handles the OrderReadyForShipping event.
    ///     Creates a shipping label via the courier adapter and publishes
    ///     a ShipmentCreatedIntegrationEvent back to Ordering.
    /// </summary>
    public async Task<ShipmentCreatedIntegrationEvent?> Handle(
        OrderReadyForShipping @event,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing OrderReadyForShipping for Order {OrderId} ({OrderNumber})",
            @event.OrderId,
            @event.OrderNumber);

        try
        {
            // Convert items to shipping DTOs
            var items = @event.Items
                .Select(i => new ShippingItemDto(
                    i.ProductId,
                    i.ProductName,
                    i.Quantity,
                    i.WeightKg))
                .ToList();

            // Call the shipping service to create label
            var result = await _shippingService.CreateLabelAsync(
                @event.OrderId,
                @event.OrderNumber,
                @event.Address,
                items,
                preferredCourier: "DHL", // Could be configurable per customer
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError(
                    "Failed to create shipping label for Order {OrderId}. Error: {Error}",
                    @event.OrderId,
                    result.Error);

                // TODO: Implement retry logic or publish a ShipmentCreationFailed event
                return null;
            }

            var label = result.Value!;

            _logger.LogInformation(
                "Shipping label created for Order {OrderId}. " +
                "TrackingNumber: {TrackingNumber}, Courier: {Courier}",
                @event.OrderId,
                label.TrackingNumber,
                label.CourierProvider);

            // Publish integration event back to Ordering
            // This will be automatically sent via Wolverine's Outbox pattern
            return new ShipmentCreatedIntegrationEvent(
                @event.OrderId,
                label.ShipmentId,
                label.TrackingNumber,
                label.CourierProvider,
                label.EstimatedDeliveryDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error processing shipping for Order {OrderId}",
                @event.OrderId);

            // Let Wolverine handle retries via its error handling policy
            throw;
        }
    }
}
