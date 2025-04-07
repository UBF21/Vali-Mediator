using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.Compensation;

/// <summary>
/// Provides a base implementation for compensable operations that can be undone or mitigated through a compensation action.
/// This abstract class implements the <see cref="ICompensable"/> interface, centralizing the logic for executing compensation.
/// </summary>
public abstract class Compensable : ICompensable
{
    public abstract IFireAndForget? GetCompensation();


    public Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default)
    {
        IFireAndForget? compensation = GetCompensation();
        if (compensation != null)
        {
            return mediator.Send(compensation, cancellationToken);
        }

        return Task.CompletedTask;
    }
}