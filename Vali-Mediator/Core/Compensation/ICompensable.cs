using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Compensation;

/// <summary>
/// Provides a base implementation for compensable operations that can be undone or mitigated through a compensation action.
/// This abstract class implements the <see cref="ICompensable"/> interface, centralizing the logic for executing compensation.
/// </summary>
public interface ICompensable
{
    /// <summary>
    /// Retrieves the compensation action to be executed when the operation fails.
    /// </summary>
    /// <returns>
    /// An <see cref="IFireAndForget"/> representing the compensation action, or <c>null</c> if no compensation is required.
    /// </returns>
    /// <remarks>
    /// Derived classes must override this method to specify the compensation action specific to their operation.
    /// Returning <c>null</c> indicates that no compensation is needed, and the <see cref="Compensate"/> method will complete without action.
    /// </remarks>
    IFireAndForget? GetCompensation();

    /// <summary>
    /// Executes the compensation action associated with this operation, if one exists.
    /// </summary>
    /// <param name="mediator">
    /// The <see cref="IValiMediator"/> instance used to dispatch the compensation action.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the compensation operation. Defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous compensation operation. If no compensation is defined, returns a completed task.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="mediator"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method calls <see cref="GetCompensation"/> to retrieve the compensation action. If the result is not <c>null</c>,
    /// it dispatches the action using the provided <paramref name="mediator"/>. Otherwise, it completes immediately.
    /// </remarks>
    Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default);
}