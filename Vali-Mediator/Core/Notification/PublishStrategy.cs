namespace Vali_Mediator.Core.Notification;

/// <summary>
/// Controls how notification handlers are invoked when publishing via
/// <see cref="Vali_Mediator.Core.General.Mediator.IValiMediator"/>.
/// </summary>
public enum PublishStrategy
{
    /// <summary>
    /// Handlers are invoked one after the other in descending <c>Priority</c> order.
    /// An exception in one handler stops execution of subsequent handlers.
    /// This is the default behavior.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// All handlers are invoked concurrently via <see cref="System.Threading.Tasks.Task.WhenAll(System.Collections.Generic.IEnumerable{System.Threading.Tasks.Task})"/>.
    /// All handlers run regardless of individual failures; all exceptions are
    /// surfaced together as an <see cref="System.AggregateException"/>.
    /// </summary>
    Parallel = 1,

    /// <summary>
    /// All handlers are invoked concurrently. Unlike <see cref="Parallel"/>, individual handler failures
    /// are captured and the remaining handlers still execute. After all handlers complete, any exceptions
    /// are thrown together as an <see cref="System.AggregateException"/>.
    /// </summary>
    ResilientParallel = 2
}
