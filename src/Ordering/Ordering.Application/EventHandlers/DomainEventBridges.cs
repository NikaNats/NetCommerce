using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Ordering.Application.EventHandlers;

/// <summary>
/// Domain event to integration event bridge for OrderPaidDomainEvent.
/// 
/// This handler listens to domain events within the Ordering module
/// and converts them to integration events that other modules can subscribe to.
/// 
/// Pattern: Ordering Module publishes OrderPaidDomainEvent internally
///         -> Bridge converts to OrderPaidIntegrationEvent
///         -> Inventory Module subscribers receive OrderPaidIntegrationEvent
/// </summary>
public sealed class OrderPaidDomainEventToBridgeHandler : INotificationHandler<OrderPaidDomainEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<OrderPaidDomainEventToBridgeHandler> _logger;

    public OrderPaidDomainEventToBridgeHandler(IMediator mediator, ILogger<OrderPaidDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderPaidDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Bridging OrderPaidDomainEvent to OrderPaidIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);

            // Convert domain event to integration event and publish
            var integrationEvent = new OrderPaidIntegrationEvent(
                notification.OrderId,
                notification.OrderNumber,
                notification.TotalAmount);

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "OrderPaidIntegrationEvent published for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging OrderPaidDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}

/// <summary>
/// Domain event to integration event bridge for OrderCreatedDomainEvent.
/// 
/// This handler listens to domain events within the Ordering module
/// and converts them to integration events that Payments module can subscribe to.
/// </summary>
public sealed class OrderCreatedDomainEventToBridgeHandler : INotificationHandler<OrderCreatedDomainEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<OrderCreatedDomainEventToBridgeHandler> _logger;

    public OrderCreatedDomainEventToBridgeHandler(IMediator mediator, ILogger<OrderCreatedDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Bridging OrderCreatedDomainEvent to OrderCreatedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);

            // Convert domain event to integration event and publish
            var integrationEvent = new OrderCreatedIntegrationEvent(
                notification.OrderId,
                notification.OrderNumber,
                notification.CustomerId);

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "OrderCreatedIntegrationEvent published for OrderId: {OrderId}",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging OrderCreatedDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}
