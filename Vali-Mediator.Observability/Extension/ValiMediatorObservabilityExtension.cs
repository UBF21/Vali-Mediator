using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator_Observability.Core.Abstractions;
using Vali_Mediator_Observability.Core.Metrics;
using Vali_Mediator_Observability.Observers;
using Vali_Mediator_Observability.Pipeline;

namespace Vali_Mediator_Observability.Extension;

/// <summary>
/// Extension methods for integrating Vali-Mediator.Observability with the DI pipeline.
/// </summary>
public static class ValiMediatorObservabilityExtension
{
    /// <summary>
    /// Registers both <see cref="ObservabilityBehavior{TRequest,TResponse}"/> (for <c>IRequest</c>) and
    /// <see cref="ObservabilityDispatchBehavior{TRequest}"/> (for <c>INotification</c> / <c>IFireAndForget</c>)
    /// as open-generic pipeline behaviors in the Vali-Mediator pipeline.
    /// </summary>
    /// <remarks>
    /// Call this inside your <c>AddValiMediator</c> configuration lambda:
    /// <code>
    /// builder.Services.AddValiMediator(config =>
    /// {
    ///     config.RegisterServicesFromAssemblyContaining&lt;Program&gt;();
    ///     config.AddObservabilityBehavior();
    /// });
    /// </code>
    /// </remarks>
    /// <param name="config">The Vali-Mediator configuration.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the registered behaviors.
    /// Defaults to <see cref="ServiceLifetime.Transient"/>.
    /// </param>
    /// <returns>The same <see cref="ValiMediatorConfiguration"/> for chaining.</returns>
    public static ValiMediatorConfiguration AddObservabilityBehavior(
        this ValiMediatorConfiguration config,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
    {
        config.AddBehavior(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<,>),
            typeof(ObservabilityBehavior<,>),
            lifetime);

        config.AddBehavior(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<>),
            typeof(ObservabilityDispatchBehavior<>),
            lifetime);

        return config;
    }

    /// <summary>
    /// Registers the core observability services: a <see cref="NoOpMetricsCollector"/> as the default
    /// <see cref="IMetricsCollector"/> and an empty <see cref="IRequestObserver"/> registration list.
    /// </summary>
    /// <remarks>
    /// Call this once on <see cref="IServiceCollection"/> (independently of <c>AddValiMediator</c>).
    /// Replace the metrics collector with <see cref="AddMetricsCollector{T}"/> or <see cref="AddConsoleMetrics"/>.
    /// Add observers with <see cref="AddRequestObserver{T}"/> or <see cref="AddConsoleLoggingObserver"/>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddObservability(this IServiceCollection services)
    {
        services.AddSingleton<IMetricsCollector, NoOpMetricsCollector>();
        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="IMetricsCollector"/> registration with a custom implementation.
    /// Any previously registered <see cref="IMetricsCollector"/> is removed first.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="IMetricsCollector"/> implementation to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the collector.
    /// Defaults to <see cref="ServiceLifetime.Singleton"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddMetricsCollector<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where T : class, IMetricsCollector
    {
        RemoveExistingMetricsCollector(services);
        services.Add(ServiceDescriptor.Describe(typeof(IMetricsCollector), typeof(T), lifetime));
        return services;
    }

    /// <summary>
    /// Adds an <see cref="IRequestObserver"/> implementation to the observer pipeline.
    /// Multiple observers may be registered; all are invoked for each request lifecycle event.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="IRequestObserver"/> implementation to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the observer.
    /// Defaults to <see cref="ServiceLifetime.Transient"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddRequestObserver<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Transient)
        where T : class, IRequestObserver
    {
        services.Add(ServiceDescriptor.Describe(typeof(IRequestObserver), typeof(T), lifetime));
        return services;
    }

    /// <summary>
    /// Replaces the <see cref="IMetricsCollector"/> with <see cref="ConsoleMetricsCollector"/>.
    /// Useful for development and debugging.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConsoleMetrics(this IServiceCollection services)
        => services.AddMetricsCollector<ConsoleMetricsCollector>();

    /// <summary>
    /// Registers <see cref="ConsoleLoggingObserver"/> as an <see cref="IRequestObserver"/>.
    /// Writes structured log lines to <see cref="Console"/> on each request lifecycle event.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddConsoleLoggingObserver(this IServiceCollection services)
        => services.AddRequestObserver<ConsoleLoggingObserver>();

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void RemoveExistingMetricsCollector(IServiceCollection services)
    {
        var existing = FindDescriptor(services, typeof(IMetricsCollector));
        if (existing is not null)
            services.Remove(existing);
    }

    private static ServiceDescriptor? FindDescriptor(IServiceCollection services, Type serviceType)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == serviceType)
                return services[i];
        }
        return null;
    }
}
