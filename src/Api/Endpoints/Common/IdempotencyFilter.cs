using Microsoft.AspNetCore.Http;
using NetCommerce.Ordering.Application.Orders.Commands;

namespace NetCommerce.Api.Endpoints.Common;

/// <summary>
///     Minimal API filter that enforces presence and format of idempotency keys
///     and injects them into commands.
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    private const string HeaderName = "X-Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency Key Required",
                detail: $"Please provide a unique identifier in the {HeaderName} header.");
        }

        var key = headerValue.ToString();
        if (!Guid.TryParse(key, out _))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Idempotency Key",
                detail: "The idempotency key must be a valid GUID.");
        }

        for (var i = 0; i < context.Arguments.Count; i++)
        {
            if (context.Arguments[i] is CreateOrderCommand createOrder)
            {
                context.Arguments[i] = createOrder with { IdempotencyKey = key };
                break;
            }
        }

        // Echo the idempotency key for observability/debugging
        context.HttpContext.Response.Headers[HeaderName] = key;

        return await next(context);
    }
}
