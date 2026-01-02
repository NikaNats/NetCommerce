using MediatR;
using Microsoft.Extensions.Logging;

namespace NetCommerce.SharedKernel.Infrastructure.Persistence.IntegrationEventLog;

/// <summary>
///     Decorator that intercepts INotificationHandler execution to log incoming integration events.
/// </summary>
public class IntegrationEventLogHandlerDecorator<TNotification> : INotificationHandler<TNotification>
    where TNotification : INotification
{
    private readonly INotificationHandler<TNotification> _inner;
    private readonly ILogger<IntegrationEventLogHandlerDecorator<TNotification>> _logger;

    public IntegrationEventLogHandlerDecorator(
        INotificationHandler<TNotification> inner,
        ILogger<IntegrationEventLogHandlerDecorator<TNotification>> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task Handle(TNotification notification, CancellationToken cancellationToken)
    {
        // 1. Pre-processing logging
        _logger.LogInformation("Handling integration event: {EventType} in Handler: {HandlerType}",
            typeof(TNotification).Name,
            _inner.GetType().Name);

        try
        {
            // 2. Execute the actual business logic
            await _inner.Handle(notification, cancellationToken);

            // 3. Success logging
            _logger.LogInformation("Successfully handled integration event: {EventType}", typeof(TNotification).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle integration event {EventType} in handler {HandlerType}",
                typeof(TNotification).Name, _inner.GetType().Name);
            throw; // Re-throw to ensure Polly/Outbox retries kick in
        }
    }
}