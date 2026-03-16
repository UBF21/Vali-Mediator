# Pipeline Behaviors

Pipeline behaviors are middleware components that wrap the handler invocation. They execute before and/or after the handler and can inspect, modify, or short-circuit the request.

---

## Two Behavior Interfaces

Vali-Mediator has two distinct pipeline behavior interfaces:

| Interface | Applies to | Signature |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | `IRequest<TResponse>` handlers | `Task<TResponse> Handle(TRequest, Func<Task<TResponse>>, CancellationToken)` |
| `IPipelineBehavior<TRequest>` | `INotification` and `IFireAndForget` handlers | `Task Handle(TRequest, Func<Task>, CancellationToken)` |

> Streaming requests (`IStreamRequest<T>`) do **not** go through the pipeline.

---

## IPipelineBehavior\<TRequest, TResponse\>

Used for `IRequest<TResponse>` handlers. Implement this interface to add cross-cutting logic around request handlers:

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
        _logger.LogInformation("Handling {Request}.", requestName);

        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        _logger.LogInformation(
            "Handled {Request} in {ElapsedMs}ms.", requestName, stopwatch.ElapsedMilliseconds);

        return response;
    }
}
```

### Timing Behavior

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
                    "Slow request detected: {Request} took {ElapsedMs}ms.",
                    typeof(TRequest).Name, sw.ElapsedMilliseconds);
            }
        }
    }
}
```

### Validation Behavior (with Vali-Validation)

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
            // Short-circuit: do not call next()
            // Return a failed Result<TResponse> if TResponse is Result<T>
            throw new ValidationException(validationResult.Errors);
        }

        return await next();
    }
}
```

---

## IPipelineBehavior\<TRequest\>

Used for `INotification` and `IFireAndForget` (dispatch types). The dispatch pipeline does not return a value:

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
        _logger.LogInformation("Dispatching {Type}.", typeof(TRequest).Name);
        await next();
        _logger.LogInformation("Dispatched {Type}.", typeof(TRequest).Name);
    }
}
```

---

## Registration Order = Pipeline Order

Behaviors are applied in the order they are registered. The **first registered** behavior is the **outermost** — it wraps everything else.

```
Registration order: Logging → Timing → Validation → Handler

Execution order:
  Logging.before
    Timing.before
      Validation.before
        Handler.Handle()
      Validation.after
    Timing.after
  Logging.after
```

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Order matters: Logging runs first (outermost)
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Dispatch behaviors for notifications and fire-and-forget
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>(ServiceLifetime.Singleton);
});
```

---

## Short-Circuiting the Pipeline

A behavior can stop the pipeline by not calling `next()`:

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
        // Check if the request requires authorization
        if (request is IRequiresAuthorization authRequest)
        {
            if (!_currentUser.HasPermission(authRequest.RequiredPermission))
            {
                // Short-circuit — do not call next()
                // If TResponse is Result<T>, we need to return a failed result
                // This requires a constraint or a throw
                throw new UnauthorizedAccessException(
                    $"Missing permission: {authRequest.RequiredPermission}");
            }
        }

        return await next();
    }
}
```

### Short-Circuiting with Result Types

When handlers return `Result<T>`, you can short-circuit without throwing by using a generic constraint:

```csharp
public class ResultValidationBehavior<TRequest, TResult, TValue>
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result<TValue>  // constrain to Result<T>
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
            // Return a typed failure without throwing
            var errors = validation.Errors.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value.ToList());

            return (TResult)(object)Result<TValue>.Fail(errors, ErrorType.Validation);
        }

        return await next();
    }
}
```

---

## Exception Handling Behavior

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
                "Unhandled exception in handler for {Request}.", typeof(TRequest).Name);
            throw;
        }
    }
}
```

---

## Summary

| Behavior interface | Applies to | next() signature |
|---|---|---|
| `IPipelineBehavior<TRequest, TResponse>` | `IRequest<TResponse>` | `Func<Task<TResponse>>` |
| `IPipelineBehavior<TRequest>` | `INotification`, `IFireAndForget` | `Func<Task>` |

| Registration method | Interface registered |
|---|---|
| `AddRequestBehavior<TImpl>()` | `IPipelineBehavior<,>` |
| `AddDispatchBehavior<TImpl>()` | `IPipelineBehavior<>` |
| `AddBehavior(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))` | `IPipelineBehavior<,>` |
| `AddBehavior(typeof(IPipelineBehavior<>), typeof(MyBehavior<>))` | `IPipelineBehavior<>` |

---

## Next Steps

- **[Processors](09-processors.md)** — Lighter alternative to full pipeline behaviors
- **[Dependency Injection](12-dependency-injection.md)** — All registration options
- **[Result](10-result.md)** — Working with Result types in behaviors
