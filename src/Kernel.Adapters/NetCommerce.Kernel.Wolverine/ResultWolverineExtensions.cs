#nullable enable

using NetCommerce.Kernel.Core.Results;
using Wolverine;

namespace NetCommerce.Kernel.Wolverine;

/// <summary>
///     Extension methods for bridging Kernel Result pattern with Wolverine pipeline control.
/// </summary>
public static class ResultWolverineExtensions
{
    /// <summary>
    /// Bridge between the Kernel Result pattern and Wolverine pipeline control.
    /// If the result is a failure, it stops the handler execution chain.
    /// </summary>
    public static HandlerContinuation ToContinuation<T>(this Result<T> result)
    {
        return result.IsSuccess ? HandlerContinuation.Continue : HandlerContinuation.Stop;
    }

    /// <summary>
    /// Bridge for parameterless Result to Wolverine pipeline control.
    /// </summary>
    public static HandlerContinuation ToContinuation(this Result result)
    {
        return result.IsSuccess ? HandlerContinuation.Continue : HandlerContinuation.Stop;
    }
}
