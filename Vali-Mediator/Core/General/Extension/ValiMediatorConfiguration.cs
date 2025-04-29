using System.Reflection;
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.Processors;
using Vali_Mediator.Core.Request;

namespace Vali_Mediator.Core.General.Extension;

/// <summary>
/// Provides configuration options for registering assemblies and pipeline behaviors with Vali-Mediator.
/// </summary>
public class ValiMediatorConfiguration
{
    private readonly List<Assembly> _assemblies = new();
    private readonly Dictionary<Type, Type> _behaviors = new();
    private readonly Dictionary<Type, Type> _preProcessors = new();
    private readonly Dictionary<Type, Type> _postProcessors = new();
    private readonly Dictionary<Type, Type> _requestPreProcessors = new();
    private readonly Dictionary<Type, Type> _requestPostProcessors = new();

    /// <summary>
    /// Registers an assembly containing request handlers or notification handlers to be scanned for services.
    /// </summary>
    /// <param name="assembly">The assembly to register.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="assembly"/> is null.</exception>
    public ValiMediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Adds a pipeline behavior to be applied to requests processed by the mediator.
    /// </summary>
    /// <param name="behaviorInterface">The interface type of the behavior (e.g., <see cref="IPipelineBehavior{TRequest,TResponse}"/>).</param>
    /// <param name="behaviorImplementation">The concrete implementation type of the behavior.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="behaviorInterface"/> or <paramref name="behaviorImplementation"/> is null.</exception>
    public ValiMediatorConfiguration AddBehavior(Type behaviorInterface, Type behaviorImplementation)
    {
        if (behaviorInterface == null) throw new ArgumentNullException(nameof(behaviorInterface));
        _behaviors[behaviorInterface] = behaviorImplementation ?? throw new ArgumentNullException(nameof(behaviorImplementation));
        return this;
    }   
    
    /// <summary>
    /// Adds a preprocessor for dispatch-able types (e.g., <see cref="INotification"/> or <see cref="IFireAndForget"/>).
    /// </summary>
    /// <param name="preProcessorInterface">The interface type of the preprocessor (e.g., <see cref="IPreProcessor{TDispatch}"/>).</param>
    /// <param name="preProcessorImplementation">The concrete implementation type of the preprocessor.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="preProcessorInterface"/> or <paramref name="preProcessorImplementation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="preProcessorInterface"/> is not of type <see cref="IPreProcessor{TDispatch}"/>.</exception>
    public ValiMediatorConfiguration AddPreProcessor(Type preProcessorInterface, Type preProcessorImplementation)
    {
        if (preProcessorInterface == null) throw new ArgumentNullException(nameof(preProcessorInterface));
        if (preProcessorImplementation == null) throw new ArgumentNullException(nameof(preProcessorImplementation));
        if (!preProcessorInterface.IsGenericType || preProcessorInterface.GetGenericTypeDefinition() != typeof(IPreProcessor<>))
            throw new ArgumentException($"Preprocessor interface must be of type IPreProcessor<T>");
        _preProcessors.Add(preProcessorInterface, preProcessorImplementation);
        return this;
    }
    
    /// <summary>
    /// Adds a postprocessor for dispatch-able types (e.g., <see cref="INotification"/> or <see cref="IFireAndForget"/>).
    /// </summary>
    /// <param name="postProcessorInterface">The interface type of the postprocessor (e.g., <see cref="IPostProcessor{TDispatch}"/>).</param>
    /// <param name="postProcessorImplementation">The concrete implementation type of the postprocessor.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="postProcessorInterface"/> or <paramref name="postProcessorImplementation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="postProcessorInterface"/> is not of type <see cref="IPostProcessor{TDispatch}"/>.</exception>
    public ValiMediatorConfiguration AddPostProcessor(Type postProcessorInterface, Type postProcessorImplementation)
    {
        if (postProcessorInterface == null) throw new ArgumentNullException(nameof(postProcessorInterface));
        if (postProcessorImplementation == null) throw new ArgumentNullException(nameof(postProcessorImplementation));
        if (!postProcessorInterface.IsGenericType || postProcessorInterface.GetGenericTypeDefinition() != typeof(IPostProcessor<>))
            throw new ArgumentException($"Postprocessor interface must be of type IPostProcessor<T>");
        _postProcessors.Add(postProcessorInterface, postProcessorImplementation);
        return this;
    }
    
    /// <summary>
    /// Adds a preprocessor for requests (e.g., <see cref="IRequest{TResponse}"/>).
    /// </summary>
    /// <param name="preProcessorInterface">The interface type of the preprocessor (e.g., <see cref="IPreProcessor{TRequest,TResponse}"/>).</param>
    /// <param name="preProcessorImplementation">The concrete implementation type of the preprocessor.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="preProcessorInterface"/> or <paramref name="preProcessorImplementation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="preProcessorInterface"/> is not of type <see cref="IPreProcessor{TRequest,TResponse}"/>.</exception>
    public ValiMediatorConfiguration AddRequestPreProcessor(Type preProcessorInterface, Type preProcessorImplementation)
    {
        if (preProcessorInterface == null) throw new ArgumentNullException(nameof(preProcessorInterface));
        if (preProcessorImplementation == null) throw new ArgumentNullException(nameof(preProcessorImplementation));
        if (!preProcessorInterface.IsGenericType || preProcessorInterface.GetGenericTypeDefinition() != typeof(IPreProcessor<,>))
            throw new ArgumentException($"Request preprocessor interface must be of type IPreProcessor<TRequest, TResponse>");
        _requestPreProcessors.Add(preProcessorInterface, preProcessorImplementation);
        return this;
    }
    
    /// <summary>
    /// Adds a postprocessor for requests (e.g., <see cref="IRequest{TResponse}"/>).
    /// </summary>
    /// <param name="postProcessorInterface">The interface type of the postprocessor (e.g., <see cref="IPostProcessor{TRequest,TResponse}"/>).</param>
    /// <param name="postProcessorImplementation">The concrete implementation type of the postprocessor.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="postProcessorInterface"/> or <paramref name="postProcessorImplementation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="postProcessorInterface"/> is not of type <see cref="IPostProcessor{TRequest,TResponse}"/>.</exception>
    public ValiMediatorConfiguration AddRequestPostProcessor(Type postProcessorInterface, Type postProcessorImplementation)
    {
        if (postProcessorInterface == null) throw new ArgumentNullException(nameof(postProcessorInterface));
        if (postProcessorImplementation == null) throw new ArgumentNullException(nameof(postProcessorImplementation));
        if (!postProcessorInterface.IsGenericType || postProcessorInterface.GetGenericTypeDefinition() != typeof(IPostProcessor<,>))
            throw new ArgumentException($"Request postprocessor interface must be of type IPostProcessor<TRequest, TResponse>");
        _requestPostProcessors.Add(postProcessorInterface, postProcessorImplementation);
        return this;
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
    
    /// <summary>
    /// Gets the dictionary of registered preprocessors for dispatch-able types.
    /// </summary>
    /// <returns>A dictionary mapping preprocessor interface types to their implementation types.</returns>
    internal IDictionary<Type, Type> GetPreProcessors() => _preProcessors;

    /// <summary>
    /// Gets the dictionary of registered postprocessors for dispatch-able types.
    /// </summary>
    /// <returns>A dictionary mapping postprocessor interface types to their implementation types.</returns>
    internal IDictionary<Type, Type> GetPostProcessors() => _postProcessors;
    
    /// <summary>
    /// Gets the dictionary of registered preprocessors for requests.
    /// </summary>
    /// <returns>A dictionary mapping preprocessor interface types to their implementation types.</returns>
    internal IDictionary<Type, Type> GetRequestPreProcessors() => _requestPreProcessors;

    /// <summary>
    /// Gets the dictionary of registered postprocessors for requests.
    /// </summary>
    /// <returns>A dictionary mapping postprocessor interface types to their implementation types.</returns>
    internal IDictionary<Type, Type> GetRequestPostProcessors() => _requestPostProcessors;
}