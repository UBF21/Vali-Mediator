# Fire and Forget

Fire-and-forget commands are one-way operations: the caller dispatches a command and does not wait for a meaningful result. They are used for side effects that should not block the main flow.

---

## IFireAndForget

Implement `IFireAndForget` to define a fire-and-forget command:

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

`IFireAndForget` inherits from `IDispatch`, which means it participates in the dispatch pipeline (behaviors and processors).

---

## IFireAndForgetHandler

Implement `IFireAndForgetHandler<TFireAndForget>` for each command:

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
        _logger.LogInformation("Welcome email sent to {Email}.", command.Email);
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

## Dispatching Fire-and-Forget Commands

Use `IValiMediator.Send(IFireAndForget)` to dispatch:

```csharp
// The await ensures the command is handed off to the handler
// (but the handler runs within the same request scope)
await _mediator.Send(new SendWelcomeEmailCommand(user.Email, user.FirstName, user.LastName), ct);
await _mediator.Send(new LogAuditEventCommand("USER_REGISTERED", user.Id.ToString(), "New user registered", DateTimeOffset.UtcNow), ct);
```

---

## Fire and Forget vs Notifications

Both `IFireAndForget` and `INotification` inherit from `IDispatch` and use the dispatch pipeline. The key difference is:

| Aspect | IFireAndForget | INotification |
|---|---|---|
| Handlers | Exactly one | Zero or more |
| Purpose | Side-effect commands | Domain events |
| Dispatch method | `Send(IFireAndForget)` | `Publish(INotification)` |
| Strategy | N/A | Sequential / Parallel / ResilientParallel |
| Throws if no handler | Yes (`HandlerNotFoundException`) | No (silently does nothing) |

Use `IFireAndForget` when:
- There is exactly one handler responsible for the operation
- The operation is a command (e.g., "send this email", "write this audit entry")
- You want the pipeline behaviors to apply

Use `INotification` when:
- Multiple handlers may react to the same event
- Any handler may or may not exist

---

## Practical Example: User Registration Flow

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
        // Check if email is already taken
        if (await _users.ExistsByEmailAsync(command.Email, ct))
            return Result<Guid>.Fail("An account with that email already exists.", ErrorType.Conflict);

        // Create the user
        var user = User.Create(command.Email, command.FirstName, command.LastName);
        await _users.SaveAsync(user, ct);

        // Fire-and-forget: send welcome email (one dedicated handler)
        await _mediator.Send(
            new SendWelcomeEmailCommand(user.Email, user.FirstName, user.LastName), ct);

        // Fire-and-forget: write audit log
        await _mediator.Send(
            new LogAuditEventCommand("USER_REGISTERED", user.Id.ToString(),
                $"User {user.Email} registered.", DateTimeOffset.UtcNow), ct);

        return user.Id;
    }
}
```

---

## Using Fire-and-Forget in Compensation

`IFireAndForget` commands are also the mechanism used by the [Compensation](11-compensation.md) system. When a saga step fails, `Compensable.Compensate()` dispatches an `IFireAndForget` rollback action via the mediator.

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

    // Return the rollback command as a fire-and-forget
    public override IFireAndForget? GetCompensation()
        => new RefundPaymentCommand(Amount, CardToken);
}
```

---

## Next Steps

- **[Notifications](05-notifications.md)** — Fan-out to multiple handlers for domain events
- **[Compensation](11-compensation.md)** — Use fire-and-forget for Saga rollbacks
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Apply dispatch behaviors to fire-and-forget
- **[Processors](09-processors.md)** — Pre/post processors for `IFireAndForget`
