using Vali_Mediator.Core.General.Exceptions;

namespace Vali_Mediator_Idempotency.Core.Exceptions;

/// <summary>
/// Thrown when an idempotency key is reused with a different request payload and the response type
/// is not an <c>IResult</c> (for which a <c>Conflict</c> failure is returned instead).
/// </summary>
public sealed class IdempotencyConflictException : ValiMediatorException
{
    /// <summary>Gets the idempotency key that was reused.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Initializes a new instance for <paramref name="idempotencyKey"/>.</summary>
    public IdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' was already used with a different request payload.")
    {
        IdempotencyKey = idempotencyKey;
    }
}
