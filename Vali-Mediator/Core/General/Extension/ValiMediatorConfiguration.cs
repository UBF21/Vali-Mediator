using System.Reflection;
using Vali_Mediator.Core.General.Behavior;

namespace Vali_Mediator.Core.General.Extension;

/// <summary>
/// Provides configuration options for registering assemblies and pipeline behaviors with Vali-Mediator.
/// </summary>
public class ValiMediatorConfiguration
{
    private readonly List<Assembly> _assemblies = new();
    private readonly Dictionary<Type, Type> _behaviors = new();

    /// <summary>
    /// Registers an assembly containing request handlers or notification handlers to be scanned for services.
    /// </summary>
    /// <param name="assembly">The assembly to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
    public void RegisterServicesFromAssembly(Assembly assembly)
    {
        _assemblies.Add(assembly);
    }

    /// <summary>
    /// Adds a pipeline behavior to be applied to requests processed by the mediator.
    /// </summary>
    /// <param name="behaviorInterface">The interface type of the behavior (e.g., <see cref="IPipelineBehavior{TRequest,TResponse}"/>).</param>
    /// <param name="behaviorImplementation">The concrete implementation type of the behavior.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="behaviorInterface"/> or <paramref name="behaviorImplementation"/> is null.</exception>
    public void AddBehavior(Type behaviorInterface, Type behaviorImplementation)
    {
        _behaviors[behaviorInterface] = behaviorImplementation;
    }

    /// <summary>
    /// Gets the list of registered assemblies.
    /// </summary>
    /// <returns>An enumerable collection of registered <see cref="Assembly"/> instances.</returns>
    internal IEnumerable<Assembly> GetAssemblies() => _assemblies;

    /// <summary>
    /// Gets the dictionary of registered pipeline behaviors.
    /// </summary>
    /// <returns>A dictionary mapping behavior interface types to their implementation types.</returns>
    internal IDictionary<Type, Type> GetBehaviors() => _behaviors;
}