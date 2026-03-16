namespace Vali_Mediator.Core.General.Exceptions;

/// <summary>
/// Thrown when no handler is registered in the DI container for a given request,
/// notification, or fire-and-forget type.
/// </summary>
public sealed class HandlerNotFoundException : ValiMediatorException
{
    /// <summary>The request/command/notification type for which no handler was found.</summary>
    public Type RequestType { get; }

    /// <param name="requestType">The dispatch type that had no registered handler.</param>
    public HandlerNotFoundException(Type requestType)
        : base($"No handler registered for '{requestType.Name}'. " +
               $"Ensure the handler is discoverable via RegisterServicesFromAssembly " +
               $"or registered explicitly in AddValiMediator.")
    {
        RequestType = requestType;
    }
}
