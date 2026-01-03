using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace NetCommerce.SharedKernel.Application.Middleware;

/// <summary>
///     Wolverine middleware for logging message handling with correlation tracking.
///     This replaces MediatR's LoggingBehavior with Wolverine's conventional middleware.
/// </summary>
public static class LoggingMiddleware
{
    /// <summary>
    ///     Before handler - logs message receipt and starts timing.
    /// </summary>
    public static void Before<T>(
        T message,
        Envelope envelope,
        ILogger<T> logger)
    {
        var messageName = typeof(T).Name;
        var correlationId = envelope.CorrelationId ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();

        logger.LogInformation(
            "Handling {MessageName} [MessageId: {MessageId}, CorrelationId: {CorrelationId}]",
            messageName,
            envelope.Id,
            correlationId);
    }

    /// <summary>
    ///     After handler - logs successful completion.
    /// </summary>
    public static void After<T>(
        T message,
        Envelope envelope,
        ILogger<T> logger)
    {
        var messageName = typeof(T).Name;
        var correlationId = envelope.CorrelationId ?? "N/A";

        logger.LogInformation(
            "Handled {MessageName} successfully [MessageId: {MessageId}, CorrelationId: {CorrelationId}]",
            messageName,
            envelope.Id,
            correlationId);
    }
}
