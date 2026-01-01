using MediatR;
using Microsoft.Extensions.Logging;
using NetCommerce.Ordering.Domain.Orders;
using NetCommerce.SharedKernel.Events;

namespace NetCommerce.Ordering.Application.EventHandlers;

/// <summary>
///     Domain event to integration event bridge for OrderSubmittedDomainEvent.
///     Triggers soft stock reservation in Inventory module during grace period.
/// </summary>
public sealed class OrderSubmittedDomainEventToBridgeHandler : INotificationHandler<OrderSubmittedDomainEvent>
{
    private readonly ILogger<OrderSubmittedDomainEventToBridgeHandler> _logger;
    private readonly IMediator _mediator;

    public OrderSubmittedDomainEventToBridgeHandler(
        IMediator mediator, 
        ILogger<OrderSubmittedDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderSubmittedDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Bridging OrderSubmittedDomainEvent to OrderSubmittedIntegrationEvent for OrderId: {OrderId}",
                notification.OrderId);

            var integrationEvent = new OrderSubmittedIntegrationEvent(
                notification.OrderId,
                notification.OrderNumber,
                notification.CustomerId);

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "OrderSubmittedIntegrationEvent published for OrderId: {OrderId}. " +
                "Inventory module will initiate soft reservation.",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging OrderSubmittedDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}

/// <summary>
///     Domain event to integration event bridge for OrderGracePeriodConfirmedDomainEvent.
///     Triggers payment capture after grace period ends.
/// </summary>
public sealed class OrderGracePeriodConfirmedDomainEventToBridgeHandler 
    : INotificationHandler<OrderGracePeriodConfirmedDomainEvent>
{
    private readonly ILogger<OrderGracePeriodConfirmedDomainEventToBridgeHandler> _logger;
    private readonly IMediator _mediator;

    public OrderGracePeriodConfirmedDomainEventToBridgeHandler(
        IMediator mediator, 
        ILogger<OrderGracePeriodConfirmedDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderGracePeriodConfirmedDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Bridging OrderGracePeriodConfirmedDomainEvent to OrderGracePeriodConfirmedIntegrationEvent " +
                "for OrderId: {OrderId}",
                notification.OrderId);

            var integrationEvent = new OrderGracePeriodConfirmedIntegrationEvent(
                notification.OrderId,
                notification.OrderNumber,
                notification.CustomerId,
                notification.TotalAmount);

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "OrderGracePeriodConfirmedIntegrationEvent published for OrderId: {OrderId}. " +
                "Payment module will initiate payment capture.",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging OrderGracePeriodConfirmedDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}

/// <summary>
///     Domain event to integration event bridge for OrderCancelledDomainEvent.
///     Triggers stock release and potential refund processing.
/// </summary>
public sealed class OrderCancelledDomainEventToBridgeHandler : INotificationHandler<OrderCancelledDomainEvent>
{
    private readonly ILogger<OrderCancelledDomainEventToBridgeHandler> _logger;
    private readonly IMediator _mediator;

    public OrderCancelledDomainEventToBridgeHandler(
        IMediator mediator, 
        ILogger<OrderCancelledDomainEventToBridgeHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OrderCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var wasInGracePeriod = notification.PreviousStatus == OrderStatus.Submitted;
            
            _logger.LogInformation(
                "Bridging OrderCancelledDomainEvent to OrderCancelledIntegrationEvent for OrderId: {OrderId}. " +
                "Previous status: {PreviousStatus}, Was in grace period: {WasInGracePeriod}",
                notification.OrderId,
                notification.PreviousStatus,
                wasInGracePeriod);

            var integrationEvent = new OrderCancelledIntegrationEvent(
                notification.OrderId,
                notification.Reason,
                notification.PreviousStatus.ToString());

            await _mediator.Publish(integrationEvent, cancellationToken);

            _logger.LogInformation(
                "OrderCancelledIntegrationEvent published for OrderId: {OrderId}. " +
                "Inventory will release stock. {RefundNote}",
                notification.OrderId,
                wasInGracePeriod ? "No refund needed (cancelled during grace period)." : "Refund may be required.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error bridging OrderCancelledDomainEvent for OrderId: {OrderId}",
                notification.OrderId);
            throw;
        }
    }
}

/// <summary>
///     Domain event to integration event bridge for OrderPaidDomainEvent.
///     This handler listens to domain events within the Ordering module
///     and converts them to integration events that other modules can subscribe to.
///     Pattern: Ordering Module publishes OrderPaidDomainEvent internally
///     -> Bridge converts to OrderPaidIntegrationEvent
///     -> Inventory Module subscribers receive OrderPaidIntegrationEvent
/// </summary>
public sealed class OrderPaidDomainEventToBridgeHandler : INotificationHandler<OrderPaidDomainEvent>
{
    private readonly ILogger<OrderPaidDomainEventToBridgeHandler> _logger;
    private readonly IMediator _mediator;

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