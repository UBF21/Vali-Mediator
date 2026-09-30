namespace Vali_Mediator.Core.General.Mediator;

/// <summary>
/// Runtime options for <see cref="ValiMediator"/>. Set through
/// <c>ValiMediatorConfiguration.SendAllMaxDegreeOfParallelism</c> when calling <c>AddValiMediator</c>.
/// </summary>
public sealed class ValiMediatorOptions
{
    /// <summary>
    /// Default limit of requests in flight for <c>SendAll(requests)</c>. <c>null</c> starts all at once.
    /// </summary>
    public int? SendAllMaxDegreeOfParallelism { get; init; }
}
