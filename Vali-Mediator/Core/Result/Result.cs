namespace Vali_Mediator.Core.Result;

/// <summary>
/// Represents the outcome of an operation that can either succeed with a value
/// or fail with an error message and type. Use <see cref="Ok"/> and <see cref="Fail(string, ErrorType)"/>
/// factory methods to construct instances.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <remarks>
/// Designed as a lightweight alternative to throwing exceptions for expected business failures.
/// Handlers can declare <c>IRequest&lt;Result&lt;T&gt;&gt;</c> and return errors without exceptions.
/// <para>
/// When <see cref="IsFailure"/> is <c>true</c>, <see cref="Value"/> is <c>default(T)</c>.
/// When <see cref="IsSuccess"/> is <c>true</c>, <see cref="Error"/> and <see cref="ErrorType"/> are <c>null</c>/<see cref="ErrorType.None"/>.
/// </para>
/// </remarks>
public readonly struct Result<T> : IResult
{
    /// <summary>The success value. Only valid when <see cref="IsSuccess"/> is <c>true</c>.</summary>
    public T? Value { get; }

    /// <summary>The human-readable error description. Only valid when <see cref="IsFailure"/> is <c>true</c>.</summary>
    public string? Error { get; }

    /// <summary>The semantic category of the failure. <see cref="ErrorType.None"/> on success.</summary>
    public ErrorType ErrorType { get; }

    /// <summary>Indicates whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indicates whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Structured validation errors keyed by property name.
    /// Only populated when the result was created via <see cref="Fail(Dictionary{string,List{string}},ErrorType)"/>.
    /// <c>null</c> for all other result types.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ValidationErrors { get; }

    private Result(T value)
    {
        Value = value;
        Error = null;
        ErrorType = ErrorType.None;
        IsSuccess = true;
        ValidationErrors = null;
    }

    private Result(string error, ErrorType errorType)
    {
        Value = default;
        Error = error;
        ErrorType = errorType;
        IsSuccess = false;
        ValidationErrors = null;
    }

    private Result(IReadOnlyDictionary<string, IReadOnlyList<string>> validationErrors)
    {
        Value = default;
        Error = "Validation failed.";
        ErrorType = ErrorType.Validation;
        IsSuccess = false;
        ValidationErrors = validationErrors;
    }

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(value);

    /// <summary>Creates a failed result with the given error message and type.</summary>
    /// <param name="error">Human-readable description of the failure.</param>
    /// <param name="errorType">Semantic category of the failure. Defaults to <see cref="ErrorType.Failure"/>.</param>
    public static Result<T> Fail(string error, ErrorType errorType = ErrorType.Failure)
        => new(error, errorType);

    /// <summary>Creates a failed result with structured validation errors (one per property).</summary>
    public static Result<T> Fail(Dictionary<string, List<string>> validationErrors, ErrorType errorType = ErrorType.Validation)
        => new(validationErrors.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly()));

    /// <summary>
    /// Implicitly converts a value of type <typeparamref name="T"/> to a successful <see cref="Result{T}"/>.
    /// Allows handlers to <c>return value;</c> instead of <c>return Result&lt;T&gt;.Ok(value);</c>.
    /// </summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>
    /// Executes <paramref name="onSuccess"/> if the result is successful, or <paramref name="onFailure"/> if it failed.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, ErrorType, TOut> onFailure)
    {
        if (onSuccess is null) throw new ArgumentNullException(nameof(onSuccess));
        if (onFailure is null) throw new ArgumentNullException(nameof(onFailure));
        return IsSuccess ? onSuccess(Value!) : onFailure(Error!, ErrorType);
    }

    /// <summary>
    /// Projects the success value to a new type. If the result is a failure, propagates the failure.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (mapper is null) throw new ArgumentNullException(nameof(mapper));
        return IsSuccess ? Result<TNew>.Ok(mapper(Value!)) : Result<TNew>.Fail(Error!, ErrorType);
    }

    /// <summary>
    /// Chains another Result-returning operation. If the result is a failure, propagates the failure.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        if (binder is null) throw new ArgumentNullException(nameof(binder));
        return IsSuccess ? binder(Value!) : Result<TNew>.Fail(Error!, ErrorType);
    }

    /// <summary>
    /// Asynchronously projects the success value to a new type. If the result is a failure, propagates the failure.
    /// </summary>
    public async Task<Result<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
    {
        if (mapper is null) throw new ArgumentNullException(nameof(mapper));
        return IsSuccess ? Result<TNew>.Ok(await mapper(Value!).ConfigureAwait(false)) : Result<TNew>.Fail(Error!, ErrorType);
    }

    /// <summary>
    /// Asynchronously chains another Result-returning operation. If the result is a failure, propagates the failure.
    /// </summary>
    public async Task<Result<TNew>> BindAsync<TNew>(Func<T, Task<Result<TNew>>> binder)
    {
        if (binder is null) throw new ArgumentNullException(nameof(binder));
        return IsSuccess ? await binder(Value!).ConfigureAwait(false) : Result<TNew>.Fail(Error!, ErrorType);
    }

    /// <summary>
    /// Executes a side effect on the success value without changing the result. Returns the same result.
    /// </summary>
    public Result<T> Tap(Action<T> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (IsSuccess) action(Value!);
        return this;
    }

    /// <summary>
    /// Executes a side effect on failure without changing the result. Returns the same result.
    /// </summary>
    public Result<T> OnFailure(Action<string, ErrorType> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (IsFailure) action(Error!, ErrorType);
        return this;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsSuccess) return $"Ok({Value})";
        if (ValidationErrors is not null) return $"ValidationFail({ValidationErrors.Count} properties)";
        return $"Fail({ErrorType}: {Error})";
    }
}
