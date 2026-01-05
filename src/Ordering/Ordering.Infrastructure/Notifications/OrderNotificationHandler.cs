#nullable enable

using Microsoft.Extensions.Logging;
using NetCommerce.SharedKernel.Application.Notifications;
using NetCommerce.SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace NetCommerce.Ordering.Infrastructure.Notifications;

/// <summary>
///     Handles order notification events using the "Event-Driven Notification Sidecar" pattern.
///     Follows 2025 best practices: Async, Decoupled, Resilient.
///
///     Key Benefits:
///     - Transactional Integrity: Email only sent if order successfully saved (Wolverine Outbox)
///     - Performance: 2-second email send doesn't block user checkout
///     - Separation of Concerns: Ordering module has zero SMTP/SendGrid knowledge
///     - Observability: CorrelationId links SendGrid logs back to Order ID
/// </summary>
[WolverineHandler]
public class OrderNotificationHandler
{
    private readonly IEmailProvider _emailProvider;
    private readonly ITemplateEngine _templates;
    private readonly ILogger<OrderNotificationHandler> _logger;

    public OrderNotificationHandler(
        IEmailProvider emailProvider,
        ITemplateEngine templates,
        ILogger<OrderNotificationHandler> logger)
    {
        _emailProvider = emailProvider;
        _templates = templates;
        _logger = logger;
    }

    /// <summary>
    ///     Wolverine automatically executes this via the Outbox after the order transaction commits.
    ///     If this fails, Wolverine will retry (3 attempts by default) and eventually move to dead letter queue.
    /// </summary>
    public async Task Handle(OrderPlacedIntegrationEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing order notification for Order {OrderId}, Customer {CustomerEmail}",
            @event.OrderId,
            @event.CustomerEmail);

        try
        {
            // 1. Render the HTML Template
            var htmlBody = await _templates.RenderAsync("OrderConfirmation", new
            {
                CustomerName = @event.CustomerName,
                OrderId = @event.OrderId,
                OrderNumber = @event.OrderNumber,
                TotalAmount = @event.TotalAmount.Amount,
                Currency = @event.TotalAmount.Currency
            }, cancellationToken);

            // 2. Send via external provider (with resilience handled by HTTP client)
            await _emailProvider.SendEmailAsync(
                @event.CustomerEmail,
                $"Order Confirmed - {@event.OrderNumber}",
                htmlBody,
                cancellationToken);

            _logger.LogInformation(
                "Order confirmation email sent successfully for Order {OrderId}",
                @event.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send order confirmation email for Order {OrderId}. Will retry via Wolverine.",
                @event.OrderId);
            throw; // Let Wolverine handle retries and dead letter queue
        }
    }
}
