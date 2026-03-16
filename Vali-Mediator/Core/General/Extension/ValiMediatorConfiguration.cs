using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Processors;

namespace Vali_Mediator.Core.General.Extension;

/// <summary>
/// Fluent configuration object for registering assemblies, behaviors, and processors with Vali-Mediator.
/// </summary>
public class ValiMediatorConfiguration
{
    private readonly List<(Assembly Assembly, ServiceLifetime Lifetime)> _assemblies = new();
    private readonly List<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> _behaviors = new();
    private readonly List<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> _preProcessors = new();
    private readonly List<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> _postProcessors = new();
    private readonly List<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> _requestPreProcessors = new();
    private readonly List<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> _requestPostProcessors = new();

    /// <summary>
    /// Registers an assembly to be scanned for handlers, processors, and stream handlers.
    /// Duplicate assemblies are ignored.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> used when registering discovered types.
    /// Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> is null.</exception>
    public ValiMediatorConfiguration RegisterServicesFromAssembly(
        Assembly assembly,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        if (_assemblies.TrueForAll(a => a.Assembly != assembly))
            _assemblies.Add((assembly, lifetime));
        return this;
    }

    /// <summary>
    /// Adds a pipeline behavior. Multiple behaviors of the same interface type are supported
    /// and will be chained in registration order (first registered = outermost in pipeline).
    /// </summary>
    /// <param name="behaviorInterface">
    /// Open-generic behavior interface: <c>typeof(IPipelineBehavior&lt;,&gt;)</c>
    /// or <c>typeof(IPipelineBehavior&lt;&gt;)</c>.
    /// </param>
    /// <param name="behaviorImplementation">The concrete open-generic implementation type.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for this behavior. Defaults to <see cref="ServiceLifetime.Scoped"/>.
    /// Use <see cref="ServiceLifetime.Singleton"/> for stateless behaviors like caching or logging.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when either type argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="behaviorInterface"/> is not a pipeline behavior interface.</exception>
    public ValiMediatorConfiguration AddBehavior(
        Type behaviorInterface,
        Type behaviorImplementation,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (behaviorInterface is null) throw new ArgumentNullException(nameof(behaviorInterface));
        if (behaviorImplementation is null) throw new ArgumentNullException(nameof(behaviorImplementation));

        if (!behaviorInterface.IsGenericType ||
            (behaviorInterface.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>) &&
             behaviorInterface.GetGenericTypeDefinition() != typeof(IPipelineBehavior<>)))
            throw new ArgumentException(
                "Behavior interface must be IPipelineBehavior<TRequest,TResponse> or IPipelineBehavior<TRequest>.",
                nameof(behaviorInterface));

        _behaviors.Add((behaviorInterface, behaviorImplementation, lifetime));
        return this;
    }

    /// <summary>
    /// Adds a preprocessor for dispatch-able types (e.g., <c>INotification</c> or <c>IFireAndForget</c>).
    /// </summary>
    /// <param name="preProcessorInterface">Closed-generic interface, e.g. <c>typeof(IPreProcessor&lt;MyNotification&gt;)</c>.</param>
    /// <param name="preProcessorImplementation">The concrete implementation type.</param>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="preProcessorInterface"/> is not <c>IPreProcessor&lt;T&gt;</c>.</exception>
    public ValiMediatorConfiguration AddPreProcessor(
        Type preProcessorInterface,
        Type preProcessorImplementation,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (preProcessorInterface is null) throw new ArgumentNullException(nameof(preProcessorInterface));
        if (preProcessorImplementation is null) throw new ArgumentNullException(nameof(preProcessorImplementation));
        if (!preProcessorInterface.IsGenericType ||
            preProcessorInterface.GetGenericTypeDefinition() != typeof(IPreProcessor<>))
            throw new ArgumentException(
                "Preprocessor interface must be IPreProcessor<TDispatch>.",
                nameof(preProcessorInterface));

        _preProcessors.Add((preProcessorInterface, preProcessorImplementation, lifetime));
        return this;
    }

    /// <summary>
    /// Adds a postprocessor for dispatch-able types (e.g., <c>INotification</c> or <c>IFireAndForget</c>).
    /// </summary>
    /// <param name="postProcessorInterface">Closed-generic interface, e.g. <c>typeof(IPostProcessor&lt;MyNotification&gt;)</c>.</param>
    /// <param name="postProcessorImplementation">The concrete implementation type.</param>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="postProcessorInterface"/> is not <c>IPostProcessor&lt;T&gt;</c>.</exception>
    public ValiMediatorConfiguration AddPostProcessor(
        Type postProcessorInterface,
        Type postProcessorImplementation,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (postProcessorInterface is null) throw new ArgumentNullException(nameof(postProcessorInterface));
        if (postProcessorImplementation is null) throw new ArgumentNullException(nameof(postProcessorImplementation));
        if (!postProcessorInterface.IsGenericType ||
            postProcessorInterface.GetGenericTypeDefinition() != typeof(IPostProcessor<>))
            throw new ArgumentException(
                "Postprocessor interface must be IPostProcessor<TDispatch>.",
                nameof(postProcessorInterface));

        _postProcessors.Add((postProcessorInterface, postProcessorImplementation, lifetime));
        return this;
    }

    /// <summary>
    /// Adds a preprocessor for request types.
    /// </summary>
    /// <param name="preProcessorInterface">Closed-generic interface, e.g. <c>typeof(IPreProcessor&lt;CreateProductCommand, int&gt;)</c>.</param>
    /// <param name="preProcessorImplementation">The concrete implementation type.</param>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="preProcessorInterface"/> is not <c>IPreProcessor&lt;TRequest,TResponse&gt;</c>.</exception>
    public ValiMediatorConfiguration AddRequestPreProcessor(
        Type preProcessorInterface,
        Type preProcessorImplementation,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (preProcessorInterface is null) throw new ArgumentNullException(nameof(preProcessorInterface));
        if (preProcessorImplementation is null) throw new ArgumentNullException(nameof(preProcessorImplementation));
        if (!preProcessorInterface.IsGenericType ||
            preProcessorInterface.GetGenericTypeDefinition() != typeof(IPreProcessor<,>))
            throw new ArgumentException(
                "Request preprocessor interface must be IPreProcessor<TRequest, TResponse>.",
                nameof(preProcessorInterface));

        _requestPreProcessors.Add((preProcessorInterface, preProcessorImplementation, lifetime));
        return this;
    }

    /// <summary>
    /// Adds a postprocessor for request types.
    /// </summary>
    /// <param name="postProcessorInterface">Closed-generic interface, e.g. <c>typeof(IPostProcessor&lt;CreateProductCommand, int&gt;)</c>.</param>
    /// <param name="postProcessorImplementation">The concrete implementation type.</param>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="postProcessorInterface"/> is not <c>IPostProcessor&lt;TRequest,TResponse&gt;</c>.</exception>
    public ValiMediatorConfiguration AddRequestPostProcessor(
        Type postProcessorInterface,
        Type postProcessorImplementation,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        if (postProcessorInterface is null) throw new ArgumentNullException(nameof(postProcessorInterface));
        if (postProcessorImplementation is null) throw new ArgumentNullException(nameof(postProcessorImplementation));
        if (!postProcessorInterface.IsGenericType ||
            postProcessorInterface.GetGenericTypeDefinition() != typeof(IPostProcessor<,>))
            throw new ArgumentException(
                "Request postprocessor interface must be IPostProcessor<TRequest, TResponse>.",
                nameof(postProcessorInterface));

        _requestPostProcessors.Add((postProcessorInterface, postProcessorImplementation, lifetime));
        return this;
    }

    /// <summary>
    /// Registers the assembly containing type <typeparamref name="T"/> for handler discovery.
    /// Convenience alternative to <see cref="RegisterServicesFromAssembly"/>.
    /// </summary>
    /// <typeparam name="T">Any type in the target assembly.</typeparam>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/> for discovered types. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    public ValiMediatorConfiguration RegisterServicesFromAssemblyContaining<T>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        => RegisterServicesFromAssembly(typeof(T).Assembly, lifetime);

    /// <summary>
    /// Adds an open-generic request pipeline behavior (for <see cref="Vali_Mediator.Core.General.Behavior.IPipelineBehavior{TRequest,TResponse}"/>).
    /// The implementation must be an open-generic type (e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c>).
    /// </summary>
    /// <typeparam name="TImplementation">The open-generic behavior implementation type.</typeparam>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    public ValiMediatorConfiguration AddRequestBehavior<TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TImplementation : class
        => AddBehavior(typeof(IPipelineBehavior<,>), typeof(TImplementation), lifetime);

    /// <summary>
    /// Adds an open-generic dispatch pipeline behavior (for <see cref="Vali_Mediator.Core.General.Behavior.IPipelineBehavior{TRequest}"/>
    /// used with <c>INotification</c> and <c>IFireAndForget</c>).
    /// The implementation must be an open-generic type (e.g. <c>typeof(LoggingBehavior&lt;&gt;)</c>).
    /// </summary>
    /// <typeparam name="TImplementation">The open-generic behavior implementation type.</typeparam>
    /// <param name="lifetime">The <see cref="ServiceLifetime"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
    public ValiMediatorConfiguration AddDispatchBehavior<TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TImplementation : class
        => AddBehavior(typeof(IPipelineBehavior<>), typeof(TImplementation), lifetime);

    /// <summary>
    /// Registers the built-in <see cref="Vali_Mediator.Core.General.Behavior.TimeoutBehavior{TRequest,TResponse}"/>
    /// for requests that implement <see cref="Vali_Mediator.Core.Request.IHasTimeout"/>.
    /// </summary>
    public ValiMediatorConfiguration AddTimeoutBehavior(
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        => AddBehavior(
            typeof(IPipelineBehavior<,>),
            typeof(Vali_Mediator.Core.General.Behavior.TimeoutBehavior<,>),
            lifetime);

    internal IReadOnlyList<(Assembly Assembly, ServiceLifetime Lifetime)> GetAssemblies() => _assemblies;
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> GetBehaviors() => _behaviors;
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> GetPreProcessors() => _preProcessors;
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> GetPostProcessors() => _postProcessors;
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> GetRequestPreProcessors() => _requestPreProcessors;
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType, ServiceLifetime Lifetime)> GetRequestPostProcessors() => _requestPostProcessors;
}
