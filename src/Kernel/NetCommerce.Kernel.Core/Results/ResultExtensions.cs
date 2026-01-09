#nullable enable

namespace NetCommerce.Kernel.Core.Results;

/// <summary>
///     Extension methods for Result type.
/// </summary>
public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> mapper)
    {
        return result.IsSuccess
            ? Result.Success(mapper(result.Value))
            : Result.Failure<TOut>(result.Error);
    }

    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> mapper)
    {
        var result = await resultTask;
        return result.Map(mapper);
    }

    public static Result<TOut> Bind<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Result<TOut>> func)
    {
        return result.IsSuccess ? func(result.Value) : Result.Failure<TOut>(result.Error);
    }

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> func)
    {
        var result = await resultTask;
        return result.IsSuccess ? await func(result.Value) : Result.Failure<TOut>(result.Error);
    }

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
    }

    public static async Task<TOut> MatchAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<Error, TOut> onFailure)
    {
        var result = await resultTask;
        return result.Match(onSuccess, onFailure);
    }

    public static Result<TIn> Tap<TIn>(
        this Result<TIn> result,
        Action<TIn> action)
    {
        if (result.IsSuccess)
        {
            action(result.Value);
        }
        return result;
    }

    public static async Task<Result<TIn>> TapAsync<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task> action)
    {
        var result = await resultTask;
        if (result.IsSuccess)
        {
            await action(result.Value);
        }
        return result;
    }

    public static Result<(T1, T2)> Combine<T1, T2>(
        this Result<T1> result1,
        Result<T2> result2)
    {
        if (result1.IsFailure)
            return Result.Failure<(T1, T2)>(result1.Error);

        if (result2.IsFailure)
            return Result.Failure<(T1, T2)>(result2.Error);

        return Result.Success((result1.Value, result2.Value));
    }
}
