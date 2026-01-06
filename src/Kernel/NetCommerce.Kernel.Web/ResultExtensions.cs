#nullable enable
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetCommerce.Kernel.Core.Results;
using System.Diagnostics;

namespace NetCommerce.Kernel.Web;

/// <summary>
///     ASP.NET Core integration extensions for Result types.
///     Optimized for 2025 Observability and RFC 9457 standards.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    ///     Converts a successful Result to an IResult.
    /// </summary>
    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToHttpResult();

    /// <summary>
    ///     Converts an Error to an IResult (Minimal API Support).
    /// </summary>
    public static IResult ToHttpResult(this Error error)
    {
        var traceId = GetTraceId();

        if (error is Rfc9457Error rfc)
        {
            // გაერთიანებული Extensions + TraceId
            var extensions = rfc.Extensions ?? new Dictionary<string, object?>();
            if (!extensions.ContainsKey("traceId")) extensions["traceId"] = traceId;

            return Results.Problem(
                detail: rfc.Detail,
                title: rfc.Title,
                type: rfc.Type,
                statusCode: rfc.Status,
                instance: rfc.Instance,
                extensions: extensions);
        }

        // Legacy Error mapping with TraceId injection
        return Results.Problem(
            detail: error.Description,
            title: error.Code,
            statusCode: error.StatusCode,
            extensions: new Dictionary<string, object?> { ["traceId"] = traceId }
        );
    }

    /// <summary>
    ///     Converts a simple Result to an IResult.
    /// </summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.Ok() : result.Error.ToHttpResult();

    /// <summary>
    ///     Manual conversion to ProblemDetails (Controller Support).
    /// </summary>
    public static ProblemDetails ToProblemDetails(this Error error)
    {
        var traceId = GetTraceId();

        var problem = new ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Code,
            Detail = error.Description
        };

        if (error is Rfc9457Error rfc)
        {
            problem.Type = rfc.Type;
            problem.Title = rfc.Title;
            problem.Detail = rfc.Detail;
            problem.Instance = rfc.Instance;
            problem.Status = rfc.Status;

            if (rfc.Extensions != null)
            {
                foreach (var ext in rfc.Extensions) problem.Extensions[ext.Key] = ext.Value;
            }
        }

        problem.Extensions["traceId"] = traceId;
        return problem;
    }

    private static string GetTraceId() =>
        Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
}
