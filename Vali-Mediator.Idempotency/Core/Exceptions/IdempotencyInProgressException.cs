using Vali_Mediator.Core.General.Exceptions;

namespace Vali_Mediator_Idempotency.Core.Exceptions;

/// <summary>
/// Thrown (for responses that are not <c>Result</c>/<c>Result&lt;T&gt;</c>) when the same idempotency key is still being
/// processed by another instance after <c>IdempotencyOptions.ReservationWaitTimeout</c>.
/// </summary>
public sealed class IdempotencyInProgressException : ValiMediatorException
{
    /// <summary>The idempotency key that is still being processed.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Creates the exception for <paramref name="idempotencyKey"/>.</summary>
    public IdempotencyInProgressException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' is still being processed by another request.")
    {
        IdempotencyKey = idempotencyKey;
    }
}
