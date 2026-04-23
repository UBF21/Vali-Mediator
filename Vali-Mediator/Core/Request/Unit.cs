namespace Vali_Mediator.Core.Request;

/// <summary>
/// Represents a void return type for requests that produce no result.
/// Use as <see cref="IRequest{Unit}"/> via the non-generic <see cref="IRequest"/> shortcut.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The singleton value of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = new();

    /// <inheritdoc/>
    public bool Equals(Unit other) => true;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;
    /// <inheritdoc/>
    public override int GetHashCode() => 0;
    /// <inheritdoc/>
    public override string ToString() => "()";
}
