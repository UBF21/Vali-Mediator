namespace Vali_Mediator.Core.General.Exceptions;

/// <summary>
/// Base class for all exceptions thrown by the Vali-Mediator pipeline.
/// Catch this type to handle any mediator-specific error in a single handler.
/// </summary>
public abstract class ValiMediatorException : Exception
{
    /// <inheritdoc cref="Exception(string)"/>
    protected ValiMediatorException(string message) : base(message) { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    protected ValiMediatorException(string message, Exception innerException)
        : base(message, innerException) { }
}
