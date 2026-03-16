# Fire and Forget

Los comandos fire-and-forget son operaciones unidireccionales: el emisor despacha un comando y no espera un resultado significativo. Se usan para efectos secundarios que no deben bloquear el flujo principal.

---

## IFireAndForget

Implementa `IFireAndForget` para definir un comando fire-and-forget:

```csharp
using Vali_Mediator.Core.FireAndForget;

public record SendWelcomeEmailCommand(
    string Email,
    string FirstName,
    string LastName) : IFireAndForget;

public record LogAuditEventCommand(
    string EventType,
    string UserId,
    string Description,
    DateTimeOffset OccurredAt) : IFireAndForget;

public record InvalidateCacheCommand(string CacheKey) : IFireAndForget;

public record NotifyExternalSystemCommand(
    string SystemId,
    string Payload) : IFireAndForget;
```

`IFireAndForget` hereda de `IDispatch`, lo que significa que participa en el pipeline de dispatch (behaviors y processors).

---

## IFireAndForgetHandler

Implementa `IFireAndForgetHandler<TFireAndForget>` para cada comando:

```csharp
public class SendWelcomeEmailHandler : IFireAndForgetHandler<SendWelcomeEmailCommand>
{
    private readonly IEmailService _email;
    private readonly ILogger<SendWelcomeEmailHandler> _logger;

    public SendWelcomeEmailHandler(IEmailService email, ILogger<SendWelcomeEmailHandler> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task Handle(SendWelcomeEmailCommand command, CancellationToken ct)
    {
        await _email.SendWelcomeAsync(command.Email, command.FirstName, command.LastName, ct);
        _logger.LogInformation("Email de bienvenida enviado a {Email}.", command.Email);
    }
}

public class LogAuditEventHandler : IFireAndForgetHandler<LogAuditEventCommand>
{
    private readonly IAuditRepository _audit;

    public LogAuditEventHandler(IAuditRepository audit) => _audit = audit;

    public async Task Handle(LogAuditEventCommand command, CancellationToken ct)
    {
        await _audit.InsertAsync(new AuditEvent(
            command.EventType,
            command.UserId,
            command.Description,
            command.OccurredAt), ct);
    }
}
```

---

## Despachando Comandos Fire-and-Forget

Usa `IValiMediator.Send(IFireAndForget)` para despachar:

```csharp
// El await garantiza que el comando sea entregado al handler
// (pero el handler se ejecuta dentro del mismo scope de petición)
await _mediator.Send(new SendWelcomeEmailCommand(user.Email, user.FirstName, user.LastName), ct);
await _mediator.Send(new LogAuditEventCommand("USER_REGISTERED", user.Id.ToString(), "Nuevo usuario registrado", DateTimeOffset.UtcNow), ct);
```

---

## Fire and Forget vs Notificaciones

Tanto `IFireAndForget` como `INotification` heredan de `IDispatch` y usan el pipeline de dispatch. La diferencia clave es:

| Aspecto | IFireAndForget | INotification |
|---|---|---|
| Handlers | Exactamente uno | Cero o más |
| Propósito | Comandos de efecto secundario | Eventos de dominio |
| Método de despacho | `Send(IFireAndForget)` | `Publish(INotification)` |
| Estrategia | N/A | Sequential / Parallel / ResilientParallel |
| Lanza si no hay handler | Sí (`HandlerNotFoundException`) | No (silenciosamente no hace nada) |

Usa `IFireAndForget` cuando:
- Hay exactamente un handler responsable de la operación
- La operación es un comando (por ejemplo, "enviar este email", "escribir esta entrada de auditoría")
- Quieres que los pipeline behaviors se apliquen

Usa `INotification` cuando:
- Múltiples handlers pueden reaccionar al mismo evento
- Puede que existan o no handlers registrados

---

## Ejemplo Práctico: Flujo de Registro de Usuario

```csharp
public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly IUserRepository _users;
    private readonly IValiMediator _mediator;

    public RegisterUserCommandHandler(IUserRepository users, IValiMediator mediator)
    {
        _users = users;
        _mediator = mediator;
    }

    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        // Verificar que el email no esté en uso
        if (await _users.ExistsByEmailAsync(command.Email, ct))
            return Result<Guid>.Fail("Ya existe una cuenta con ese email.", ErrorType.Conflict);

        // Crear el usuario
        var user = User.Create(command.Email, command.FirstName, command.LastName);
        await _users.SaveAsync(user, ct);

        // Fire-and-forget: enviar email de bienvenida (un handler dedicado)
        await _mediator.Send(
            new SendWelcomeEmailCommand(user.Email, user.FirstName, user.LastName), ct);

        // Fire-and-forget: escribir log de auditoría
        await _mediator.Send(
            new LogAuditEventCommand("USER_REGISTERED", user.Id.ToString(),
                $"Usuario {user.Email} registrado.", DateTimeOffset.UtcNow), ct);

        return user.Id;
    }
}
```

---

## Uso en Compensación

Los comandos `IFireAndForget` también son el mecanismo usado por el sistema de [Compensación](11-compensacion.md). Cuando falla un paso del saga, `Compensable.Compensate()` despacha una acción de rollback `IFireAndForget` a través del mediator.

```csharp
public class ChargePaymentCommand : Compensable, IRequest<Result<PaymentId>>
{
    public decimal Amount { get; }
    public string CardToken { get; }

    public ChargePaymentCommand(decimal amount, string cardToken)
    {
        Amount = amount;
        CardToken = cardToken;
    }

    // Devuelve el comando de rollback como fire-and-forget
    public override IFireAndForget? GetCompensation()
        => new RefundPaymentCommand(Amount, CardToken);
}
```

---

## Siguientes Pasos

- **[Notificaciones](05-notificaciones.md)** — Fan-out a múltiples handlers para eventos de dominio
- **[Compensación](11-compensacion.md)** — Usar fire-and-forget para rollbacks en Saga
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Aplicar dispatch behaviors a fire-and-forget
- **[Procesadores](09-procesadores.md)** — Pre/post processors para `IFireAndForget`
