using FluentValidation;

namespace NetCommerce.Api.Endpoints.Common;

public class ValidationFilter<T>(IValidator<T>? validator = null) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (validator is null) return await next(context);

        var request = context.Arguments.OfType<T>().FirstOrDefault();
        if (request is null) return await next(context);

        var validationResult = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        return await next(context);
    }
}
