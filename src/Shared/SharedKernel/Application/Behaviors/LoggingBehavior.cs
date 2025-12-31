using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace NetCommerce.SharedKernel.Application.Behaviors;

/// <summary>
/// Pipeline behavior for logging requests with correlation tracking.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Handling {RequestName} with CorrelationId: {CorrelationId}",
            requestName,
            correlationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "Handled {RequestName} with CorrelationId: {CorrelationId} in {ElapsedMs}ms",
                requestName,
                correlationId,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(
                ex,
                "Error handling {RequestName} with CorrelationId: {CorrelationId} after {ElapsedMs}ms",
                requestName,
                correlationId,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }
}
