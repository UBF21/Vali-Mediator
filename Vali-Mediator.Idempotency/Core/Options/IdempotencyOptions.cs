namespace Vali_Mediator_Idempotency.Core.Options;

/// <summary>
/// Limits and safety switches for <c>IdempotencyBehavior</c>.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// Maximum length of <c>IdempotencyKey</c> and <c>IdempotencyScope</c> (each). Longer values are
    /// rejected with an <see cref="ArgumentException"/>. Default: 256.
    /// </summary>
    public int MaxKeyLength
    {
        get => _maxKeyLength;
        set => _maxKeyLength = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(MaxKeyLength), value, "Must be greater than zero.");
    }

    private int _maxKeyLength = 256;

    /// <summary>
    /// When <c>true</c> (default) a SHA-256 fingerprint of the serialized request is stored, and the same key
    /// arriving later with a different payload is treated as a conflict.
    /// Disable it if requests carry per-attempt volatile fields (timestamps, correlation ids).
    /// </summary>
    public bool VerifyRequestFingerprint { get; set; } = true;

    /// <summary>
    /// Lifetime of the cross-process reservation taken while the handler runs (only for stores whose
    /// <c>SupportsReservation</c> is <c>true</c>). It must exceed the handler's worst-case duration: if it expires
    /// first, another instance may start the same work. Default: 30 seconds.
    /// </summary>
    public TimeSpan ReservationLease
    {
        get => _reservationLease;
        set => _reservationLease = Positive(value, nameof(ReservationLease));
    }

    private TimeSpan _reservationLease = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a caller waits for another instance that holds the reservation before giving up with a
    /// "still in progress" conflict. Default: 30 seconds.
    /// </summary>
    public TimeSpan ReservationWaitTimeout
    {
        get => _reservationWaitTimeout;
        set => _reservationWaitTimeout = Positive(value, nameof(ReservationWaitTimeout));
    }

    private TimeSpan _reservationWaitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often a waiting caller checks whether the other instance finished. Default: 25 ms.</summary>
    public TimeSpan ReservationPollInterval
    {
        get => _reservationPollInterval;
        set => _reservationPollInterval = Positive(value, nameof(ReservationPollInterval));
    }

    private TimeSpan _reservationPollInterval = TimeSpan.FromMilliseconds(25);

    private static TimeSpan Positive(TimeSpan value, string name)
        => value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "Must be greater than zero.");
}
