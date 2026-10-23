namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// Separates expected business-rule failures (<see cref="Failure(Error)"/>) from technical
/// exceptions, which keep being thrown (conventions-backend.md §3). Handlers return a
/// <see cref="Result"/>/<see cref="Result{T}"/> for the former and let unexpected failures
/// propagate as exceptions.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, error: null);

    public static Result Failure(Error error)
    {
        Guard.Against.Null(error, nameof(error));

        return new Result(false, error);
    }

    public static Result<T> Success<T>(T value) => new(value, true, error: null);

    public static Result<T> Failure<T>(Error error)
    {
        Guard.Against.Null(error, nameof(error));

        return new Result<T>(default, false, error);
    }
}

/// <summary>
/// A <see cref="Result"/> that carries a value on success. Accessing <see cref="Value"/> when
/// <see cref="Result.IsFailure"/> is a programming error (the caller forgot to check the
/// result first), so it throws rather than silently returning <c>default</c> - a silent
/// default would hide the bug instead of surfacing it at the point of misuse.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot access {nameof(Value)} of a failed result (error '{Error!.Code}': {Error.Message}). " +
            $"Check {nameof(IsSuccess)}/{nameof(IsFailure)} before accessing {nameof(Value)}.");
}
