# Pipeline Behaviors

Los pipeline behaviors son componentes middleware que envuelven la invocación del handler. Se ejecutan antes y/o después del handler, y pueden inspeccionar, modificar o cortocircuitar la petición.

---

## Dos Interfaces de Behavior

Vali-Mediator tiene dos interfaces de pipeline behavior distintas:

| Interfaz | Aplica a | Firma |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | Handlers `IRequest<TResponse>` | `Task<TResponse> Handle(TRequest, Func<Task<TResponse>>, CancellationToken)` |
| `IPipelineBehavior<TRequest>` | Handlers `INotification` e `IFireAndForget` | `Task Handle(TRequest, Func<Task>, CancellationToken)` |

> Las peticiones de streaming (`IStreamRequest<T>`) **no** pasan por el pipeline.

---

## IPipelineBehavior\<TRequest, TResponse\>

Se usa para handlers `IRequest<TResponse>`. Implementa esta interfaz para agregar lógica transversal alrededor de los handlers de peticiones:

```csharp
using Vali_Mediator.Core.General.Behavior;
using Vali_Mediator.Core.Request;

public class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Procesando {Request}.", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        _logger.LogInformation(
            "Procesado {Request} en {ElapsedMs}ms.", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
```

### Behavior de Timing

```csharp
public class TimingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TimingBehavior<TRequest, TResponse>> _logger;

    public TimingBehavior(ILogger<TimingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning(
                    "Petición lenta detectada: {Request} tardó {ElapsedMs}ms.",
                    typeof(TRequest).Name, sw.ElapsedMilliseconds);
            }
        }
    }
}
```

### Behavior de Validación (con Vali-Validation)

```csharp
public class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest>? _validator;

    public ValidationBehavior(IValidator<TRequest>? validator = null)
        => _validator = validator;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        if (_validator is null)
            return await next();

        var validationResult = await _validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            // Cortocircuito: no llama a next()
            throw new ValidationException(validationResult.Errors);
        }

        return await next();
    }
}
```

---

## IPipelineBehavior\<TRequest\>

Se usa para `INotification` e `IFireAndForget` (tipos dispatch). El pipeline de dispatch no devuelve valor:

```csharp
public class NotificationLoggingBehavior<TRequest>
    : IPipelineBehavior<TRequest>
    where TRequest : IDispatch
{
    private readonly ILogger<NotificationLoggingBehavior<TRequest>> _logger;

    public NotificationLoggingBehavior(ILogger<NotificationLoggingBehavior<TRequest>> logger)
        => _logger = logger;

    public async Task Handle(TRequest request, Func<Task> next, CancellationToken ct)
    {
        _logger.LogInformation("Despachando {Type}.", typeof(TRequest).Name);
        await next();
        _logger.LogInformation("Despachado {Type}.", typeof(TRequest).Name);
    }
}
```

---

## Orden de Registro = Orden del Pipeline

Los behaviors se aplican en el orden en que se registran. El **primero en registrarse** es el **más externo** — envuelve a todo lo demás.

```
Orden de registro: Logging → Timing → Validation → Handler

Orden de ejecución:
  Logging.antes
    Timing.antes
      Validation.antes
        Handler.Handle()
      Validation.después
    Timing.después
  Logging.después
```

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // El orden importa: Logging se ejecuta primero (más externo)
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Dispatch behaviors para notificaciones y fire-and-forget
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>(ServiceLifetime.Singleton);
});
```

---

## Cortocircuitar el Pipeline

Un behavior puede detener el pipeline si no llama a `next()`:

```csharp
public class AuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ICurrentUser _currentUser;

    public AuthorizationBehavior(ICurrentUser currentUser) => _currentUser = currentUser;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        // Verificar si la petición requiere autorización
        if (request is IRequiresAuthorization authRequest)
        {
            if (!_currentUser.HasPermission(authRequest.RequiredPermission))
            {
                // Cortocircuito — no llama a next()
                throw new UnauthorizedAccessException(
                    $"Permiso faltante: {authRequest.RequiredPermission}");
            }
        }

        return await next();
    }
}
```

### Cortocircuitar con Tipos Result

Cuando los handlers devuelven `Result<T>`, puedes cortocircuitar sin lanzar excepciones usando una constraint genérica:

```csharp
public class ResultValidationBehavior<TRequest, TResult, TValue>
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result<TValue>  // constraint a Result<T>
{
    private readonly IValidator<TRequest>? _validator;

    public ResultValidationBehavior(IValidator<TRequest>? validator = null)
        => _validator = validator;

    public async Task<TResult> Handle(
        TRequest request,
        Func<Task<TResult>> next,
        CancellationToken ct)
    {
        if (_validator is null)
            return await next();

        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            // Devuelve un fallo tipado sin lanzar excepción
            var errors = validation.Errors.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value.ToList());

            return (TResult)(object)Result<TValue>.Fail(errors, ErrorType.Validation);
        }

        return await next();
    }
}
```

---

## Behavior de Manejo de Excepciones

```csharp
public class ExceptionHandlingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> _logger;

    public ExceptionHandlingBehavior(
        ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Excepción no manejada en el handler para {Request}.", typeof(TRequest).Name);
            throw;
        }
    }
}
```

---

## Resumen

| Interfaz de behavior | Aplica a | Firma de next() |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | `IRequest<TResponse>` | `Func<Task<TResponse>>` |
| `IPipelineBehavior<TRequest>` | `INotification`, `IFireAndForget` | `Func<Task>` |

| Método de registro | Interfaz registrada |
|---|---|
| `AddRequestBehavior<TImpl>()` | `IPipelineBehavior<,>` |
| `AddDispatchBehavior<TImpl>()` | `IPipelineBehavior<>` |
| `AddBehavior(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))` | `IPipelineBehavior<,>` |
| `AddBehavior(typeof(IPipelineBehavior<>), typeof(MyBehavior<>))` | `IPipelineBehavior<>` |

---

## Siguientes Pasos

- **[Procesadores](09-procesadores.md)** — Alternativa más ligera a los pipeline behaviors completos
- **[Inyección de Dependencias](12-inyeccion-dependencias.md)** — Todas las opciones de registro
- **[Result](10-resultado.md)** — Trabajar con tipos Result en behaviors
