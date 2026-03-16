namespace Vali_Mediator.Core.Result;

/// <summary>
/// Classifies the semantic category of a <see cref="Result{T}"/> failure.
/// </summary>
public enum ErrorType
{
    /// <summary>No error — the result is successful.</summary>
    None = 0,

    /// <summary>Input data failed business or format validation rules.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist.</summary>
    NotFound = 2,

    /// <summary>The operation conflicts with the current state (e.g., duplicate).</summary>
    Conflict = 3,

    /// <summary>The caller is not authenticated.</summary>
    Unauthorized = 4,

    /// <summary>The caller is authenticated but lacks permission.</summary>
    Forbidden = 5,

    /// <summary>A general, unclassified failure.</summary>
    Failure = 6
}
