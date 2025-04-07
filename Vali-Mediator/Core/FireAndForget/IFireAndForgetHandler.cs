using Vali_Mediator.Core.General.Mediator;

namespace Vali_Mediator.Core.FireAndForget;

/// <summary>
/// Defines a handler for processing fire-and-forget commands of type <typeparamref name="TFireAndForget"/>.
/// This interface is used by the <see cref="IValiMediator"/> to dispatch commands that implement <see cref="IFireAndForget"/>.
/// </summary>
/// <typeparam name="TFireAndForget">
/// The type of the fire-and-forget command to handle, which must implement <see cref="IFireAndForget"/>.
/// </typeparam>
/// <remarks>
/// Implementations of this interface are responsible for executing the logic associated with a fire-and-forget command.
/// These commands are typically asynchronous operations that modify state or trigger side effects without returning a response.
/// </remarks>
public interface IFireAndForgetHandler<in TFireAndForget>
    where TFireAndForget : IFireAndForget
{
    /// <summary>
    /// Handles the specified fire-and-forget command asynchronously.
    /// </summary>
    /// <param name="fireAndForget">
    /// The fire-and-forget command instance to process.
    /// </param>
    /// <param name="cancellationToken">
    /// A <see cref="CancellationToken"/> that can be used to cancel the handling operation.
    /// </param>
    /// <returns>
    /// A <see cref="Task"/> representing the asynchronous handling operation.
    /// </returns>
    Task Handle(TFireAndForget fireAndForget, CancellationToken cancellationToken);

}