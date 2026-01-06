#region

using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.Domain.Shared.Events;

#endregion

namespace NetCommerce.Ordering.Application.Handlers;

/// <summary>
///     Handler that publishes OrderReadyForShipping event when order is fulfilled.
///     This triggers the Shipping module to create labels asynchronously.
/// </summary>
public sealed class PublishOrderReadyForShippingHandler
{
    private readonly ILogger<PublishOrderReadyForShippingHandler> _logger;
    private readonly IOrderRepository _orderRepository;

    public PublishOrderReadyForShippingHandler(
        IOrderRepository orderRepository,
        ILogger<PublishOrderReadyForShippingHandler> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }

    /// <summary>
    ///     Handles FinalizeOrderCommand and publishes OrderReadyForShipping.
    ///     This is triggered after the saga completes successfully (payment + inventory confirmed).
    /// </summary>
    public async Task<OrderReadyForShipping?> Handle(
        FinalizeOrderCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Order {OrderId} finalized. Fetching order details for shipping.",
            command.OrderId);

        Order? order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning(
                "Order {OrderId} not found. Cannot publish OrderReadyForShipping.",
                command.OrderId);
            return null;
        }

        var shippingItems = order.Items.Select(item => new ShippingItem(
            item.ProductId,
            item.AppliedTitle,
            item.Quantity,
            item.AppliedWeightKg)).ToList();

        var shippingAddress = new ShippingAddressDto(
            order.ShippingAddress.RecipientName,
            order.ShippingAddress.Street,
            order.ShippingAddress.City,
            order.ShippingAddress.State,
            order.ShippingAddress.Country,
            order.ShippingAddress.PostalCode,
            order.ShippingAddress.Phone);

        _logger.LogInformation(
            "Publishing OrderReadyForShipping for Order {OrderId} ({OrderNumber}) with {ItemCount} items, total weight {TotalWeight}kg",
            order.Id,
            order.OrderNumber,
            shippingItems.Count,
            shippingItems.Sum(i => i.WeightKg * i.Quantity));

        return new OrderReadyForShipping(
            order.Id,
            order.OrderNumber,
            shippingItems,
            shippingAddress);
    }
}
