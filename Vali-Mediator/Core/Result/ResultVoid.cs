namespace Vali_Mediator.Core.Result;

/// <summary>
/// Represents the outcome of an operation that either succeeds (with no value) or fails.
/// Use for void-returning handlers that may fail with an error.
/// </summary>
/// <remarks>
/// Use <see cref="Ok()"/> and <see cref="Fail(string, ErrorType)"/> factory methods to construct instances.
/// To return a value on success, use <see cref="Result{T}"/> instead.
/// </remarks>
public readonly struct Result : IResult, IEquatable<Result>
{
    /// <summary>The human-readable error description. Only valid when <see cref="IsFailure"/> is true.</summary>
    public string? Error { get; }

    /// <summary>The semantic category of the failure. <see cref="ErrorType.None"/> on success.</summary>
    public ErrorType ErrorType { get; }

    /// <summary>Indicates whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indicates whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    private Result(bool isSuccess, string? error, ErrorType errorType)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorType = errorType;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Ok() => new(true, null, ErrorType.None);

    /// <summary>Creates a failed result with the given error message and type.</summary>
    public static Result Fail(string error, ErrorType errorType = ErrorType.Failure)
        => new(false, error, errorType);

    /// <summary>
    /// Projects this result to a typed <see cref="Result{T}"/> by applying <paramref name="mapper"/>
    /// when the result is successful.
    /// </summary>
    public Result<T> Map<T>(Func<T> mapper)
    {
        if (mapper is null) throw new ArgumentNullException(nameof(mapper));
        return IsSuccess ? Result<T>.Ok(mapper()) : Result<T>.Fail(Error!, ErrorType);
    }

    /// <summary>
    /// Executes one of two actions depending on success or failure.
    /// </summary>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<string, ErrorType, TOut> onFailure)
    {
        if (onSuccess is null) throw new ArgumentNullException(nameof(onSuccess));
        if (onFailure is null) throw new ArgumentNullException(nameof(onFailure));
        return IsSuccess ? onSuccess() : onFailure(Error!, ErrorType);
    }

    /// <summary>Executes a side effect on success. Returns the same result.</summary>
    public Result Tap(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (IsSuccess) action();
        return this;
    }

    /// <summary>Executes a side effect on failure. Returns the same result.</summary>
    public Result OnFailure(Action<string, ErrorType> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (IsFailure) action(Error!, ErrorType);
        return this;
    }

    public bool Equals(Result other) => IsSuccess == other.IsSuccess && Error == other.Error && ErrorType == other.ErrorType;
    public override bool Equals(object? obj) => obj is Result r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error, ErrorType);
    public override string ToString() => IsSuccess ? "Ok()" : $"Fail({ErrorType}: {Error})";
}
