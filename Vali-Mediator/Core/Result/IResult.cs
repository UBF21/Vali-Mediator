namespace Vali_Mediator.Core.Result;

/// <summary>
/// Common contract for both <see cref="Result{T}"/> and <see cref="Result"/>.
/// Allows code that is agnostic to the success-value type (e.g. resilience policies,
/// logging behaviours) to inspect outcome without reflection or generics.
/// </summary>
public interface IResult
{
    /// <summary>Indicates whether the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>Indicates whether the operation failed.</summary>
    bool IsFailure { get; }

    /// <summary>Semantic category of the failure. <see cref="ErrorType.None"/> on success.</summary>
    ErrorType ErrorType { get; }

    /// <summary>Human-readable error description. <c>null</c> on success.</summary>
    string? Error { get; }
}
