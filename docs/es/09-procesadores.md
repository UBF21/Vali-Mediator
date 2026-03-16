# Procesadores

Los procesadores son hooks ligeros que se ejecutan antes o después de un handler sin necesitar la infraestructura completa de un pipeline behavior. Se descubren automáticamente desde el assembly registrado.

---

## Dos Tipos de Procesadores

| Interfaz | Cuándo se ejecuta | Aplica a |
|---|---|---|
| `IPreProcessor<TDispatch>` | Antes del handler | `INotification`, `IFireAndForget` |
| `IPreProcessor<TRequest, TResponse>` | Antes del handler | `IRequest<TResponse>` |
| `IPostProcessor<TDispatch>` | Después del handler | `INotification`, `IFireAndForget` |
| `IPostProcessor<TRequest, TResponse>` | Después del handler | `IRequest<TResponse>` |

---

## IPreProcessor\<TDispatch\>

Para tipos dispatch (`INotification`, `IFireAndForget`):

```csharp
using Vali_Mediator.Core.Processors;

// Se ejecuta antes de cada handler de OrderPlacedNotification
public class OrderPlacedAuditPreProcessor : IPreProcessor<OrderPlacedNotification>
{
    private readonly IAuditService _audit;

    public OrderPlacedAuditPreProcessor(IAuditService audit) => _audit = audit;

    public async Task Process(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _audit.RecordAsync(
            "ORDER_PLACED_NOTIFICATION_DISPATCHED",
            notification.OrderId.ToString(),
            ct);
    }
}

// Se ejecuta antes de cada envío de email fire-and-forget
public class EmailLoggingPreProcessor : IPreProcessor<SendWelcomeEmailCommand>
{
    private readonly ILogger<EmailLoggingPreProcessor> _logger;

    public EmailLoggingPreProcessor(ILogger<EmailLoggingPreProcessor> logger)
        => _logger = logger;

    public Task Process(SendWelcomeEmailCommand command, CancellationToken ct)
    {
        _logger.LogDebug("Enviando email de bienvenida a {Email}.", command.Email);
        return Task.CompletedTask;
    }
}
```

---

## IPreProcessor\<TRequest, TResponse\>

Para tipos request/response:

```csharp
// Se ejecuta antes de que CreateOrderCommand sea manejado
public class CreateOrderPreProcessor : IPreProcessor<CreateOrderCommand, Result<Guid>>
{
    private readonly ILogger<CreateOrderPreProcessor> _logger;
    private readonly ICurrentUser _currentUser;

    public CreateOrderPreProcessor(
        ILogger<CreateOrderPreProcessor> logger,
        ICurrentUser currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public Task Process(CreateOrderCommand request, CancellationToken ct)
    {
        _logger.LogInformation(
            "El usuario {UserId} está creando un pedido con {ItemCount} ítems.",
            _currentUser.UserId, request.Items.Count);

        return Task.CompletedTask;
    }
}
```

---

## IPostProcessor\<TDispatch\>

Para tipos dispatch — se ejecuta después de que todos los handlers hayan completado:

```csharp
// Se ejecuta después de cada handler de OrderPlacedNotification
public class OrderPlacedMetricsPostProcessor : IPostProcessor<OrderPlacedNotification>
{
    private readonly IMetricsService _metrics;

    public OrderPlacedMetricsPostProcessor(IMetricsService metrics) => _metrics = metrics;

    public async Task Process(OrderPlacedNotification notification, CancellationToken ct)
    {
        await _metrics.IncrementAsync("notifications.order_placed", ct);
    }
}
```

---

## IPostProcessor\<TRequest, TResponse\>

Para tipos request/response — recibe tanto la petición original como la respuesta del handler:

```csharp
// Se ejecuta después de que CreateOrderCommand sea manejado — registra el resultado
public class CreateOrderPostProcessor : IPostProcessor<CreateOrderCommand, Result<Guid>>
{
    private readonly ILogger<CreateOrderPostProcessor> _logger;

    public CreateOrderPostProcessor(ILogger<CreateOrderPostProcessor> logger)
        => _logger = logger;

    public Task Process(CreateOrderCommand request, Result<Guid> response, CancellationToken ct)
    {
        if (response.IsSuccess)
            _logger.LogInformation("Pedido {OrderId} creado exitosamente.", response.Value);
        else
            _logger.LogWarning("Fallo al crear pedido: {Error} ({ErrorType}).",
                response.Error, response.ErrorType);

        return Task.CompletedTask;
    }
}

// Invalidar caché después de actualizar un producto
public class UpdateProductCachePostProcessor
    : IPostProcessor<UpdateProductCommand, Result>
{
    private readonly ICacheService _cache;

    public UpdateProductCachePostProcessor(ICacheService cache) => _cache = cache;

    public async Task Process(UpdateProductCommand request, Result response, CancellationToken ct)
    {
        if (response.IsSuccess)
            await _cache.InvalidateAsync($"product:{request.ProductId}", ct);
    }
}
```

---

## Auto-Descubrimiento

Los procesadores que implementan cualquiera de las cuatro interfaces son **descubiertos automáticamente** cuando `RegisterServicesFromAssembly` escanea el assembly. No se requiere registro explícito:

```csharp
builder.Services.AddValiMediator(config =>
{
    // Todas las implementaciones de IPreProcessor<> e IPostProcessor<> en este assembly
    // se descubren y registran automáticamente
    config.RegisterServicesFromAssemblyContaining<Program>();
});
```

---

## Registro Explícito

Si necesitas registrar un procesador desde un assembly diferente o con un lifetime específico, usa los métodos explícitos:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Registro explícito para un tipo dispatch específico
    config.AddPreProcessor(
        typeof(IPreProcessor<OrderPlacedNotification>),
        typeof(OrderPlacedAuditPreProcessor));

    // Registro explícito para un tipo de petición específico
    config.AddRequestPreProcessor(
        typeof(IPreProcessor<CreateOrderCommand, Result<Guid>>),
        typeof(CreateOrderPreProcessor));

    config.AddRequestPostProcessor(
        typeof(IPostProcessor<CreateOrderCommand, Result<Guid>>),
        typeof(CreateOrderPostProcessor),
        ServiceLifetime.Singleton);
});
```

---

## Procesadores vs Behaviors

| Aspecto | Procesadores | Behaviors |
|---|---|---|
| Interfaz | `IPreProcessor<T>`, `IPostProcessor<T>` | `IPipelineBehavior<TRequest, TResponse>` |
| Posición | Antes o después (hooks separados) | Envuelve toda la llamada (antes + después en una sola clase) |
| Cortocircuito | No — los procesadores no pueden detener el pipeline | Sí — no llamando a `next()` |
| Auto-descubrimiento | Sí | No — siempre se registran explícitamente |
| Acceso a la respuesta | Solo `IPostProcessor<TRequest, TResponse>` | Sí — control total |
| Mejor para | Logging simple, métricas, invalidación de caché | Validación, autorización, manejo de excepciones |

---

## Ejemplo Completo: Ciclo de Vida de una Petición

```csharp
// 1. PreProcessor se ejecuta
public class CreateProductPreProcessor : IPreProcessor<CreateProductCommand, Result<int>>
{
    public Task Process(CreateProductCommand request, CancellationToken ct)
    {
        // Enriquecer, registrar o validar antes del handler
        return Task.CompletedTask;
    }
}

// 2. Handler se ejecuta
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<int>>
{
    public async Task<Result<int>> Handle(CreateProductCommand command, CancellationToken ct)
    {
        // Lógica de negocio
        return 42;
    }
}

// 3. PostProcessor se ejecuta
public class CreateProductPostProcessor : IPostProcessor<CreateProductCommand, Result<int>>
{
    public Task Process(CreateProductCommand request, Result<int> response, CancellationToken ct)
    {
        // Registrar, notificar o invalidar cachés después del handler
        return Task.CompletedTask;
    }
}
```

El orden de ejecución para un pipeline completo con behaviors y procesadores:

```
Behavior 1 (antes)
  Behavior 2 (antes)
    PreProcessors
      Handler
    PostProcessors
  Behavior 2 (después)
Behavior 1 (después)
```

---

## Siguientes Pasos

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Interfaz de behavior completa con soporte de cortocircuito
- **[Inyección de Dependencias](12-inyeccion-dependencias.md)** — Opciones de registro explícito de procesadores
- **[Notificaciones](05-notificaciones.md)** — Procesadores aplicados a tipos dispatch
