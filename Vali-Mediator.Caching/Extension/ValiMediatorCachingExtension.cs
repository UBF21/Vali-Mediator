using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator_Caching.Core.Abstractions;
using Vali_Mediator_Caching.Core.Store;
using Vali_Mediator_Caching.Pipeline;

namespace Vali_Mediator_Caching.Extension;

/// <summary>
/// Extension methods for registering Vali-Mediator caching services.
/// </summary>
public static class ValiMediatorCachingExtension
{
    /// <summary>
    /// Registers both <see cref="CachingBehavior{TRequest,TResponse}"/> and
    /// <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/> as open-generic
    /// pipeline behaviors in the Vali-Mediator pipeline.
    /// </summary>
    /// <remarks>
    /// Call this inside your <c>AddValiMediator</c> configuration lambda:
    /// <code>
    /// builder.Services.AddValiMediator(config =>
    /// {
    ///     config.RegisterServicesFromAssemblyContaining&lt;Program&gt;();
    ///     config.AddCachingBehavior();
    /// });
    /// </code>
    /// You must also register an <see cref="ICacheStore"/> implementation, e.g.:
    /// <code>
    /// builder.Services.AddInMemoryCacheStore();
    /// </code>
    /// </remarks>
    /// <param name="config">The Vali-Mediator configuration instance.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the behavior registrations.
    /// Defaults to <see cref="ServiceLifetime.Singleton"/> because behaviors are stateless.
    /// </param>
    public static ValiMediatorConfiguration AddCachingBehavior(
        this ValiMediatorConfiguration config,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        config.AddBehavior(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<,>),
            typeof(CachingBehavior<,>),
            lifetime);

        config.AddBehavior(
            typeof(Vali_Mediator.Core.General.Behavior.IPipelineBehavior<,>),
            typeof(CacheInvalidationBehavior<,>),
            lifetime);

        return config;
    }

    /// <summary>
    /// Registers <see cref="InMemoryCacheStore"/> as the singleton <see cref="ICacheStore"/>
    /// implementation with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddInMemoryCacheStore(this IServiceCollection services)
    {
        services.AddSingleton<ICacheStore, InMemoryCacheStore>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="InMemoryCacheStore"/> as the singleton <see cref="ICacheStore"/>
    /// implementation with custom <see cref="InMemoryCacheOptions"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Delegate to configure <see cref="InMemoryCacheOptions"/>.</param>
    public static IServiceCollection AddInMemoryCacheStore(
        this IServiceCollection services,
        Action<InMemoryCacheOptions> configureOptions)
    {
        if (configureOptions is null) throw new ArgumentNullException(nameof(configureOptions));

        var options = new InMemoryCacheOptions();
        configureOptions(options);
        services.AddSingleton<ICacheStore>(new InMemoryCacheStore(options));
        return services;
    }

    /// <summary>
    /// Registers a custom <typeparamref name="TStore"/> as the singleton <see cref="ICacheStore"/>.
    /// </summary>
    /// <typeparam name="TStore">
    /// The custom store type, which must implement <see cref="ICacheStore"/>.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.
    /// </param>
    public static IServiceCollection AddCacheStore<TStore>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TStore : class, ICacheStore
    {
        services.Add(new ServiceDescriptor(typeof(ICacheStore), typeof(TStore), lifetime));
        return services;
    }
}
