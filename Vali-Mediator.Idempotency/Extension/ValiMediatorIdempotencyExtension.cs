using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator_Idempotency.Core.Abstractions;
using Vali_Mediator_Idempotency.Core.Serialization;
using Vali_Mediator_Idempotency.Core.Store;
using Vali_Mediator_Idempotency.Pipeline;

namespace Vali_Mediator_Idempotency.Extension;

/// <summary>
/// Extension methods for integrating the idempotency pipeline into Vali-Mediator.
/// </summary>
public static class ValiMediatorIdempotencyExtension
{
    /// <summary>
    /// Registers the open-generic <see cref="IdempotencyBehavior{TRequest,TResponse}"/> as the
    /// outermost pipeline behavior in the Vali-Mediator request pipeline.
    /// </summary>
    /// <param name="config">The Vali-Mediator configuration being built.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the behavior.
    /// Defaults to <see cref="ServiceLifetime.Singleton"/> because the behavior holds only
    /// stateless dependencies (the store and serializer are resolved separately).
    /// </param>
    /// <returns>The same <paramref name="config"/> for chaining.</returns>
    public static ValiMediatorConfiguration AddIdempotencyBehavior(
        this ValiMediatorConfiguration config,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        config.AddBehavior(
            typeof(IPipelineBehavior<,>),
            typeof(IdempotencyBehavior<,>),
            lifetime);

        return config;
    }

    /// <summary>
    /// Registers the <see cref="InMemoryIdempotencyStore"/> as the <see cref="IIdempotencyStore"/> singleton
    /// and the <see cref="JsonIdempotencySerializer"/> as the <see cref="IIdempotencySerializer"/> singleton.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IIdempotencySerializer, JsonIdempotencySerializer>();

        return services;
    }

    /// <summary>
    /// Registers a custom <typeparamref name="TStore"/> as the <see cref="IIdempotencyStore"/> singleton.
    /// </summary>
    /// <typeparam name="TStore">The custom store implementation type.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIdempotencyStore<TStore>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TStore : class, IIdempotencyStore
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.Add(ServiceDescriptor.Describe(typeof(IIdempotencyStore), typeof(TStore), lifetime));

        return services;
    }

    /// <summary>
    /// Replaces the default <see cref="IIdempotencySerializer"/> registration with a custom
    /// <typeparamref name="TSerializer"/> implementation.
    /// </summary>
    /// <typeparam name="TSerializer">The custom serializer implementation type.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="lifetime">
    /// The <see cref="ServiceLifetime"/> for the serializer. Defaults to <see cref="ServiceLifetime.Singleton"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddIdempotencySerializer<TSerializer>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TSerializer : class, IIdempotencySerializer
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.Add(ServiceDescriptor.Describe(typeof(IIdempotencySerializer), typeof(TSerializer), lifetime));

        return services;
    }
}
