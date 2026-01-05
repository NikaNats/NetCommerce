using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;
using Wolverine;

namespace NetCommerce.SharedKernel.Infrastructure.Observability;

/// <summary>
/// 2025 Structured Correlation: "The Detective View"
///
/// Key Principle: "Every log must contain the Business Correlation.
/// If you search for an Order ID in Seq, you should see:
/// - The HTTP Request (API)
/// - The Saga State Change (Ordering)
/// - The Outbox Message persistence (Wolverine)
/// - The actual HTTP call to Stripe (Resilience Handler logs)"
///
/// Uses OpenTelemetry Activity to propagate correlation across:
/// - HTTP boundaries (W3C Trace Context)
/// - Message boundaries (Wolverine correlation)
/// - Database transactions (Entity Framework)
/// - External API calls (HttpClient)
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Get current correlation ID (from HTTP request or Wolverine message).
    /// </summary>
    string GetCorrelationId();

    /// <summary>
    /// Get current business context (Order ID, Customer ID, etc.).
    /// </summary>
    BusinessCorrelationContext GetBusinessContext();

    /// <summary>
    /// Enrich current Activity with business context (Order ID, etc.).
    /// </summary>
    void EnrichWithBusinessContext(string orderId, string? customerId = null);

    /// <summary>
    /// Create a new Activity span for a business operation.
    /// </summary>
    Activity? StartBusinessActivity(string operationName, ActivityKind kind = ActivityKind.Internal);
}

/// <summary>
/// Business correlation context (enriched on Activity tags).
/// </summary>
public record BusinessCorrelationContext
{
    public string CorrelationId { get; init; } = string.Empty;
    public string? OrderId { get; init; }
    public string? CustomerId { get; init; }
    public string? SagaId { get; init; }
    public string? PaymentId { get; init; }
    public string? ShipmentId { get; init; }
}

/// <summary>
/// Correlation service implementation using OpenTelemetry Activity.
/// </summary>
public sealed class OpenTelemetryCorrelationService : ICorrelationService
{
    private const string OrderIdTag = "business.order_id";
    private const string CustomerIdTag = "business.customer_id";
    private const string SagaIdTag = "business.saga_id";
    private const string PaymentIdTag = "business.payment_id";
    private const string ShipmentIdTag = "business.shipment_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<OpenTelemetryCorrelationService> _logger;
    private static readonly ActivitySource _activitySource = new("NetCommerce.BusinessOperations");

    public OpenTelemetryCorrelationService(
        IHttpContextAccessor httpContextAccessor,
        ILogger<OpenTelemetryCorrelationService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public string GetCorrelationId()
    {
        // Priority 1: OpenTelemetry TraceId (automatically propagated)
        var activity = Activity.Current;
        if (activity != null)
            return activity.TraceId.ToString();

        // Priority 2: HTTP Request correlation header
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId) == true)
            return correlationId.ToString();

        // Fallback: Generate new correlation ID
        return Guid.NewGuid().ToString("N");
    }

    public BusinessCorrelationContext GetBusinessContext()
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            return new BusinessCorrelationContext
            {
                CorrelationId = GetCorrelationId()
            };
        }

        return new BusinessCorrelationContext
        {
            CorrelationId = activity.TraceId.ToString(),
            OrderId = activity.GetTagItem(OrderIdTag)?.ToString(),
            CustomerId = activity.GetTagItem(CustomerIdTag)?.ToString(),
            SagaId = activity.GetTagItem(SagaIdTag)?.ToString(),
            PaymentId = activity.GetTagItem(PaymentIdTag)?.ToString(),
            ShipmentId = activity.GetTagItem(ShipmentIdTag)?.ToString()
        };
    }

    public void EnrichWithBusinessContext(string orderId, string? customerId = null)
    {
        var activity = Activity.Current;
        if (activity == null)
        {
            _logger.LogWarning(
                "Cannot enrich business context: No active Activity. OrderId: {OrderId}",
                orderId);
            return;
        }

        // Add business context as Activity tags (propagated to all child spans)
        activity.SetTag(OrderIdTag, orderId);
        if (!string.IsNullOrEmpty(customerId))
            activity.SetTag(CustomerIdTag, customerId);

        // Add baggage for cross-boundary propagation (HTTP, message bus)
        activity.SetBaggage("order_id", orderId);
        if (!string.IsNullOrEmpty(customerId))
            activity.SetBaggage("customer_id", customerId);

        _logger.LogDebug(
            "Business context enriched: OrderId={OrderId}, CustomerId={CustomerId}, TraceId={TraceId}",
            orderId, customerId, activity.TraceId);
    }

    public Activity? StartBusinessActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
    {
        var activity = _activitySource.StartActivity(operationName, kind);

        // Propagate business context from parent activity
        var parentActivity = Activity.Current;
        if (parentActivity != null)
        {
            var orderId = parentActivity.GetTagItem(OrderIdTag)?.ToString();
            var customerId = parentActivity.GetTagItem(CustomerIdTag)?.ToString();

            if (!string.IsNullOrEmpty(orderId))
                activity?.SetTag(OrderIdTag, orderId);
            if (!string.IsNullOrEmpty(customerId))
                activity?.SetTag(CustomerIdTag, customerId);
        }

        return activity;
    }
}

/// <summary>
/// Middleware to automatically extract correlation ID from HTTP headers
/// and enrich OpenTelemetry Activity.
/// </summary>
public sealed class CorrelationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationMiddleware> _logger;

    public CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract correlation ID from request header (or generate new one)
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                           ?? Guid.NewGuid().ToString("N");

        // Add correlation ID to response header
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        // Enrich OpenTelemetry Activity
        var activity = Activity.Current;
        if (activity != null)
        {
            activity.SetTag("http.correlation_id", correlationId);
            activity.SetBaggage("correlation_id", correlationId);
        }

        // Add correlation ID to HttpContext items (accessible in controllers)
        context.Items["CorrelationId"] = correlationId;

        // Add correlation ID to logger scope (all logs in this request will have it)
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path,
            ["RequestMethod"] = context.Request.Method
        }))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Wolverine extension to enrich Activity with Saga/Message context.
/// </summary>
public static class WolverineCorrelationExtensions
{
    /// <summary>
    /// Enrich Activity with Saga ID (called at start of Saga handler).
    /// </summary>
    public static void EnrichWithSagaContext(this Activity? activity, Guid sagaId, string sagaType)
    {
        if (activity == null)
            return;

        activity.SetTag("business.saga_id", sagaId.ToString());
        activity.SetTag("business.saga_type", sagaType);
        activity.SetBaggage("saga_id", sagaId.ToString());
    }

    /// <summary>
    /// Enrich Activity with Message context (called by Wolverine middleware).
    /// </summary>
    public static void EnrichWithMessageContext(
        this Activity? activity,
        string messageType,
        Guid messageId,
        string? conversationId = null)
    {
        if (activity == null)
            return;

        activity.SetTag("messaging.message_type", messageType);
        activity.SetTag("messaging.message_id", messageId.ToString());

        if (!string.IsNullOrEmpty(conversationId))
        {
            activity.SetTag("messaging.conversation_id", conversationId);
            activity.SetBaggage("conversation_id", conversationId);
        }
    }

    /// <summary>
    /// Enrich Activity with Payment context.
    /// </summary>
    public static void EnrichWithPaymentContext(
        this Activity? activity,
        Guid paymentId,
        string paymentProvider,
        decimal amount)
    {
        if (activity == null)
            return;

        activity.SetTag("business.payment_id", paymentId.ToString());
        activity.SetTag("business.payment_provider", paymentProvider);
        activity.SetTag("business.payment_amount", amount);
    }

    /// <summary>
    /// Enrich Activity with Shipment context.
    /// </summary>
    public static void EnrichWithShipmentContext(
        this Activity? activity,
        Guid shipmentId,
        string carrier,
        string trackingNumber)
    {
        if (activity == null)
            return;

        activity.SetTag("business.shipment_id", shipmentId.ToString());
        activity.SetTag("business.shipping_carrier", carrier);
        activity.SetTag("business.tracking_number", trackingNumber);
    }
}

/// <summary>
/// Extension methods for dependency injection.
/// </summary>
public static class CorrelationServiceExtensions
{
    public static IServiceCollection AddCorrelationService(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationService, OpenTelemetryCorrelationService>();
        return services;
    }

    public static IApplicationBuilder UseCorrelationMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationMiddleware>();
        return app;
    }
}

/// <summary>
/// Serilog enricher for correlation context.
/// </summary>
public sealed class BusinessCorrelationEnricher : Serilog.Core.ILogEventEnricher
{
    public void Enrich(Serilog.Events.LogEvent logEvent, Serilog.Core.ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null)
            return;

        // Add all business tags to log properties
        var orderId = activity.GetTagItem("business.order_id")?.ToString();
        if (!string.IsNullOrEmpty(orderId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("OrderId", orderId));

        var customerId = activity.GetTagItem("business.customer_id")?.ToString();
        if (!string.IsNullOrEmpty(customerId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CustomerId", customerId));

        var sagaId = activity.GetTagItem("business.saga_id")?.ToString();
        if (!string.IsNullOrEmpty(sagaId))
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SagaId", sagaId));

        // Add OpenTelemetry trace context
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
