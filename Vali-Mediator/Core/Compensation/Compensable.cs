using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Compensation;

/// <summary>
/// Base class for operations that support compensation (Saga pattern).
/// Override <see cref="GetCompensation"/> to return the rollback action.
/// </summary>
public abstract class Compensable : ICompensable
{
    /// <inheritdoc/>
    public abstract IFireAndForget? GetCompensation();

    /// <inheritdoc/>
    public Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default)
    {
        var compensation = GetCompensation();
        return compensation is not null
            ? mediator.Send(compensation, cancellationToken)
            : Task.CompletedTask;
    }
}
