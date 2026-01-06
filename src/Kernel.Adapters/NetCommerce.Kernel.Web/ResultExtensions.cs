#nullable enable
using Microsoft.AspNetCore.Http;

namespace NetCommerce.Kernel.Web;

/// <summary>
/// Extensions for converting Kernel Results to ASP.NET Core HTTP responses.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a Result to an ASP.NET Core IResult using RFC 9457 Problem Details.
    /// </summary>
    public static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess) return Results.Ok();

        var error = result.Error;

        // Convert legacy Error to RFC 9457 format
        var rfc9457Error = new Rfc9457Error(
            Type: $"https://netcommerce.io/probs/{error.Code.ToLowerInvariant().Replace('.', '-')}",
            Title: error.Code.Split('.').LastOrDefault() ?? "Error",
            Status: MapErrorCodeToStatusCode(error.Code),
            Detail: error.Description
        );

        // Microsoft.AspNetCore.Http.Results.Problem-ის გამოყენება RFC 9457-ის მხარდასაჭერად
        return Results.Problem(
            type: rfc9457Error.Type,
            title: rfc9457Error.Title,
            detail: rfc9457Error.Detail,
            statusCode: rfc9457Error.Status,
            instance: rfc9457Error.Instance,
            extensions: rfc9457Error.Extensions?.ToDictionary(k => k.Key, v => v.Value)
        );
    }

    /// <summary>
    /// Converts a Result&lt;T&gt; to an ASP.NET Core IResult.
    /// </summary>
    public static IResult ToResult<T>(this Result<T> result)
    {
        if (result.IsSuccess) return Results.Ok(result.Value);
        return result.ToProblemDetails();
    }

    /// <summary>
    /// Converts a Result&lt;T&gt; to an ASP.NET Core IResult with custom success status code.
    /// </summary>
    public static IResult ToResult<T>(this Result<T> result, int statusCode)
    {
        if (result.IsSuccess) return Results.Json(result.Value, statusCode: statusCode);
        return result.ToProblemDetails();
    }

    private static int MapErrorCodeToStatusCode(string errorCode)
    {
        return errorCode.ToLowerInvariant() switch
        {
            var c when c.Contains("notfound") => 404,
            var c when c.Contains("validation") => 422,
            var c when c.Contains("conflict") => 409,
            var c when c.Contains("unauthorized") => 401,
            var c when c.Contains("forbidden") => 403,
            var c when c.Contains("nullvalue") => 422,
            _ => 500
        };
    }
}

/// <summary>
/// Extensions for Error type to convert to RFC 9457 Problem Details.
/// </summary>
public static class ErrorExtensions
{
    /// <summary>
    /// Converts an Error to an ASP.NET Core IResult using RFC 9457 Problem Details.
    /// </summary>
    public static IResult ToProblemDetails(this NetCommerce.Kernel.Core.Results.Error error)
    {
        // Convert legacy Error to RFC 9457 format
        var rfc9457Error = new NetCommerce.Kernel.Core.Results.Rfc9457Error(
            Type: $"https://netcommerce.io/probs/{error.Code.ToLowerInvariant().Replace('.', '-')}",
            Title: error.Code.Split('.').LastOrDefault() ?? "Error",
            Status: MapErrorCodeToStatusCode(error.Code),
            Detail: error.Description
        );

        // Microsoft.AspNetCore.Http.Results.Problem-ის გამოყენება RFC 9457-ის მხარდასაჭერად
        return Results.Problem(
            type: rfc9457Error.Type,
            title: rfc9457Error.Title,
            detail: rfc9457Error.Detail,
            statusCode: rfc9457Error.Status,
            instance: rfc9457Error.Instance,
            extensions: rfc9457Error.Extensions?.ToDictionary(k => k.Key, v => v.Value)
        );
    }

    private static int MapErrorCodeToStatusCode(string errorCode)
    {
        return errorCode.ToLowerInvariant() switch
        {
            var c when c.Contains("notfound") => 404,
            var c when c.Contains("validation") => 422,
            var c when c.Contains("conflict") => 409,
            var c when c.Contains("unauthorized") => 401,
            var c when c.Contains("forbidden") => 403,
            var c when c.Contains("nullvalue") => 422,
            _ => 500
        };
    }
}</content>
<parameter name="filePath">c:\Users\Nika\source\repos\NikaNats\NetCommerce\src\Kernel.Adapters\NetCommerce.Kernel.Web\ResultExtensions.cs
