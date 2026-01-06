using Microsoft.AspNetCore.Http;
using NetCommerce.Kernel.Core.Results;

namespace NetCommerce.Kernel.Web;

/// <summary>
///     ASP.NET Core integration extensions for Result types.
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
    ///     Converts an Error to an IResult.
    /// </summary>
    public static IResult ToHttpResult(this Error error) =>
        error switch
        {
            Rfc9457Error rfc9457Error => Results.Problem(
                detail: rfc9457Error.Detail,
                title: rfc9457Error.Title,
                type: rfc9457Error.Type,
                statusCode: rfc9457Error.Status,
                instance: rfc9457Error.Instance),
            _ => Results.Problem(
                detail: error.Description,
                title: error.Code,
                statusCode: 500)
        };

    /// <summary>
    ///     Converts a Result to an IResult.
    /// </summary>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess
            ? Results.Ok()
            : result.Error.ToHttpResult();
}
