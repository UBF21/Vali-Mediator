# Dependency Injection

This article covers the complete reference for registering Vali-Mediator services with the .NET DI container.

---

## AddValiMediator

The entry point for registration. Call it once in `Program.cs` (or `Startup.cs`):

```csharp
using Vali_Mediator.Core.General.Extension;

builder.Services.AddValiMediator(config =>
{
    // Configuration goes here
});
```

`AddValiMediator`:
1. Registers `IValiMediator` as **Scoped** (one instance per request scope)
2. Scans the specified assemblies for handlers, processors, and stream handlers
3. Registers all explicitly specified behaviors and processors

---

## Assembly Registration

### RegisterServicesFromAssembly

Scans an assembly and registers all discovered handlers:

```csharp
config.RegisterServicesFromAssembly(typeof(Program).Assembly);

// With a custom lifetime (default is Scoped)
config.RegisterServicesFromAssembly(
    typeof(Program).Assembly,
    ServiceLifetime.Transient);
```

Discovered types:
- `IRequestHandler<TRequest, TResponse>`
- `INotificationHandler<TNotification>`
- `IFireAndForgetHandler<TFireAndForget>`
- `IStreamRequestHandler<TRequest, TResponse>`
- `IPreProcessor<TDispatch>` and `IPreProcessor<TRequest, TResponse>`
- `IPostProcessor<TDispatch>` and `IPostProcessor<TRequest, TResponse>`

### RegisterServicesFromAssemblyContaining\<T\>

Convenience overload that resolves the assembly from a type:

```csharp
// Registers the assembly that contains CreateOrderHandler
config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();

// Useful when handlers are in a separate Application project
config.RegisterServicesFromAssemblyContaining<ApplicationModule>();

// With a custom lifetime
config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>(ServiceLifetime.Singleton);
```

### Multiple Assemblies

```csharp
config.RegisterServicesFromAssemblyContaining<OrdersApplicationModule>();
config.RegisterServicesFromAssemblyContaining<InventoryApplicationModule>();
config.RegisterServicesFromAssemblyContaining<NotificationsApplicationModule>();
```

Duplicate assemblies are detected and ignored automatically.

---

## Behavior Registration

### AddRequestBehavior\<TImplementation\>

Registers an open-generic behavior for `IRequest<TResponse>` handlers:

```csharp
// TImplementation must be an open-generic type
config.AddRequestBehavior<LoggingBehavior<,>>();
config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
config.AddRequestBehavior<ValidationBehavior<,>>();
```

Equivalent to:

```csharp
config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### AddDispatchBehavior\<TImplementation\>

Registers an open-generic behavior for `INotification` and `IFireAndForget` handlers:

```csharp
config.AddDispatchBehavior<NotificationLoggingBehavior<>>();
config.AddDispatchBehavior<AuditBehavior<>>(ServiceLifetime.Singleton);
```

Equivalent to:

```csharp
config.AddBehavior(typeof(IPipelineBehavior<>), typeof(NotificationLoggingBehavior<>));
```

### AddBehavior (low-level)

The explicit API that `AddRequestBehavior` and `AddDispatchBehavior` delegate to:

```csharp
// For IRequest<TResponse> behaviors
config.AddBehavior(
    typeof(IPipelineBehavior<,>),
    typeof(LoggingBehavior<,>),
    ServiceLifetime.Singleton);

// For INotification / IFireAndForget behaviors
config.AddBehavior(
    typeof(IPipelineBehavior<>),
    typeof(NotificationLoggingBehavior<>));
```

`AddBehavior` validates that the interface type is a valid pipeline behavior interface and throws `ArgumentException` if not.

---

## Processor Registration

### Auto-Discovery (recommended)

Processors are automatically discovered when `RegisterServicesFromAssembly` scans the assembly. No explicit registration needed:

```csharp
config.RegisterServicesFromAssemblyContaining<Program>();
// All IPreProcessor<> and IPostProcessor<> implementations are discovered automatically
```

### Explicit Registration

Use explicit registration when processors live in a different assembly or require a specific lifetime:

```csharp
// IPreProcessor<TDispatch> — for INotification or IFireAndForget
config.AddPreProcessor(
    typeof(IPreProcessor<OrderPlacedNotification>),
    typeof(OrderPlacedAuditPreProcessor));

// IPostProcessor<TDispatch>
config.AddPostProcessor(
    typeof(IPostProcessor<OrderPlacedNotification>),
    typeof(OrderMetricsPostProcessor),
    ServiceLifetime.Singleton);

// IPreProcessor<TRequest, TResponse> — for IRequest<TResponse>
config.AddRequestPreProcessor(
    typeof(IPreProcessor<CreateOrderCommand, Result<Guid>>),
    typeof(CreateOrderValidationPreProcessor));

// IPostProcessor<TRequest, TResponse>
config.AddRequestPostProcessor(
    typeof(IPostProcessor<CreateOrderCommand, Result<Guid>>),
    typeof(CreateOrderCachePostProcessor),
    ServiceLifetime.Scoped);
```

---

## ServiceLifetime

All registration methods accept an optional `ServiceLifetime` parameter:

| Lifetime | Description | Recommended for |
|---|---|---|
| `Scoped` (default) | One instance per request scope | Handlers that use repositories or DbContext |
| `Transient` | New instance per use | Lightweight, stateless handlers |
| `Singleton` | One instance for the application lifetime | Stateless behaviors (logging, timing, caching) |

> `IValiMediator` is always registered as **Scoped**, regardless of the handler lifetime.

```csharp
builder.Services.AddValiMediator(config =>
{
    // Handlers: Scoped (default) — access DbContext per request
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Stateless behaviors: Singleton — avoid allocation on every request
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);

    // Stateful behavior: Scoped — uses per-request services
    config.AddRequestBehavior<ValidationBehavior<,>>(ServiceLifetime.Scoped);

    // Dispatch behaviors
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>(ServiceLifetime.Singleton);
});
```

---

## Complete Program.cs Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    // Handler assemblies
    config.RegisterServicesFromAssemblyContaining<CreateOrderCommandHandler>();
    config.RegisterServicesFromAssemblyContaining<InventoryHandler>();

    // Request pipeline: Logging → Timing → Validation → Handler
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Dispatch pipeline: Logging → Handler
    config.AddDispatchBehavior<DispatchLoggingBehavior<>>(ServiceLifetime.Singleton);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Fluent Configuration API Summary

| Method | Registers | Notes |
|---|---|---|
| `RegisterServicesFromAssembly(assembly, lifetime)` | All handlers and auto-discovered processors from the assembly | Duplicates ignored |
| `RegisterServicesFromAssemblyContaining<T>(lifetime)` | Same as above, resolves assembly from `typeof(T)` | Convenience overload |
| `AddRequestBehavior<TImpl>(lifetime)` | `IPipelineBehavior<,>` | For `IRequest<TResponse>` pipeline |
| `AddDispatchBehavior<TImpl>(lifetime)` | `IPipelineBehavior<>` | For `INotification` and `IFireAndForget` pipeline |
| `AddBehavior(interface, impl, lifetime)` | Either behavior interface | Low-level; validates the interface type |
| `AddPreProcessor(interface, impl, lifetime)` | `IPreProcessor<TDispatch>` | Explicit processor for dispatch types |
| `AddPostProcessor(interface, impl, lifetime)` | `IPostProcessor<TDispatch>` | Explicit post-processor for dispatch types |
| `AddRequestPreProcessor(interface, impl, lifetime)` | `IPreProcessor<TRequest, TResponse>` | Explicit pre-processor for request types |
| `AddRequestPostProcessor(interface, impl, lifetime)` | `IPostProcessor<TRequest, TResponse>` | Explicit post-processor for request types |

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Implement `IPipelineBehavior`
- **[Processors](09-processors.md)** — Implement `IPreProcessor` / `IPostProcessor`
- **[ASP.NET Core Integration](13-aspnetcore-integration.md)** — Map results to HTTP responses
