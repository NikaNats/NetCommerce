#nullable enable
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine.Middleware;

/// <summary>
///     Wolverine middleware for logging message handling with correlation tracking.
/// </summary>
public static class LoggingMiddleware
{
    /// <summary>
    ///     Before handler - logs message receipt.
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
