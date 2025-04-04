namespace Vali_Mediator.Core.FireAndForget;

public interface IFireAndForgetHandler<in TFireAndForget>
    where TFireAndForget : IFireAndForget
{
    Task Handle(TFireAndForget command, CancellationToken cancellationToken);

}