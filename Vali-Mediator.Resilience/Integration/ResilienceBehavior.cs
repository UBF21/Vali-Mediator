using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;
using Vali_Mediator_Resilience.Core.Policies;

namespace Vali_Mediator_Resilience.Integration;

/// <summary>
/// Vali-Mediator pipeline behaviour that automatically applies a <see cref="ResiliencePolicy"/>
/// to any <c>IRequest&lt;TResponse&gt;</c> that implements <see cref="IResilient"/>.
/// </summary>
/// <remarks>
/// Register via <c>config.AddResilienceBehavior()</c> in <c>AddValiMediator</c>.
/// Because <c>next</c> does not accept a <see cref="System.Threading.CancellationToken"/>,
/// timeout uses Pessimistic strategy (Task.WhenAny) within the behaviour.
/// For optimistic timeout, pass the CancellationToken into the handler directly via the command/query.
/// </remarks>
public sealed class ResilienceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (request is not IResilient resilient)
            return await next().ConfigureAwait(false);

        var policy = resilient.Policy;

        return await policy.ExecuteAsync(
            _ => next(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
