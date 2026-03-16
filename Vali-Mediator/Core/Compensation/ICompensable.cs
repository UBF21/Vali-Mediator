using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Compensation;

/// <summary>
/// Defines an operation that can be compensated (undone or mitigated) upon failure.
/// </summary>
public interface ICompensable
{
    /// <summary>
    /// Returns the compensation action to execute when this operation fails,
    /// or <c>null</c> if no compensation is required.
    /// </summary>
    IFireAndForget? GetCompensation();

    /// <summary>
    /// Executes the compensation action, if one exists.
    /// </summary>
    /// <param name="mediator">The mediator used to dispatch the compensation action.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default);
}
