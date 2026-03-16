# Compensation

Compensation enables the Saga pattern: when a step in a multi-step operation fails, the already-completed steps are rolled back by executing their compensation actions.

---

## The Problem: Distributed Transactions

In a microservice or multi-service architecture, there is no single database transaction that spans all operations. If step 3 of a 5-step process fails, steps 1 and 2 must be explicitly undone:

```
1. Reserve inventory    ✓
2. Charge payment       ✓
3. Create shipment      ✗  ← fails here
   → Compensate step 2: Refund payment
   → Compensate step 1: Release inventory
```

Vali-Mediator's compensation model supports this pattern through `ICompensable` and the `Compensable` base class.

---

## ICompensable

```csharp
public interface ICompensable
{
    // Returns the compensation action, or null if no compensation is needed
    IFireAndForget? GetCompensation();

    // Dispatches the compensation action via the mediator
    Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default);
}
```

The compensation action is an `IFireAndForget` command that the mediator dispatches when called.

---

## Compensable Base Class

`Compensable` provides the `Compensate` implementation — you only need to override `GetCompensation`:

```csharp
public abstract class Compensable : ICompensable
{
    public abstract IFireAndForget? GetCompensation();

    public Task Compensate(IValiMediator mediator, CancellationToken ct = default)
    {
        var compensation = GetCompensation();
        return compensation is not null
            ? mediator.Send(compensation, ct)
            : Task.CompletedTask;
    }
}
```

---

## Defining Compensable Commands

Make a request implement `Compensable` and return its rollback as an `IFireAndForget`:

```csharp
// Fire-and-forget rollback commands
public record ReleaseInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItem> Items) : IFireAndForget;

public record RefundPaymentCommand(
    Guid OrderId,
    decimal Amount,
    string TransactionId) : IFireAndForget;

// Step 1: Reserve inventory (compensable)
public record ReserveInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItem> Items)
    : Compensable, IRequest<Result<InventoryReservation>>
{
    public override IFireAndForget? GetCompensation()
        => new ReleaseInventoryCommand(OrderId, Items);
}

// Step 2: Charge payment (compensable)
public record ChargePaymentCommand(
    Guid OrderId,
    decimal Amount,
    string CardToken)
    : Compensable, IRequest<Result<PaymentTransaction>>
{
    public override IFireAndForget? GetCompensation()
        => new RefundPaymentCommand(OrderId, Amount, string.Empty);
        // Note: TransactionId is not available at construction time.
        // In real scenarios, store the transaction ID after step 2 succeeds
        // and use it in the compensation.
}
```

---

## Compensation Handlers

Each rollback command needs a handler:

```csharp
public class ReleaseInventoryHandler : IFireAndForgetHandler<ReleaseInventoryCommand>
{
    private readonly IInventoryService _inventory;
    private readonly ILogger<ReleaseInventoryHandler> _logger;

    public ReleaseInventoryHandler(IInventoryService inventory, ILogger<ReleaseInventoryHandler> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    public async Task Handle(ReleaseInventoryCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Releasing inventory for order {OrderId}.", command.OrderId);
        await _inventory.ReleaseReservationAsync(command.OrderId, command.Items, ct);
    }
}

public class RefundPaymentHandler : IFireAndForgetHandler<RefundPaymentCommand>
{
    private readonly IPaymentService _payment;

    public RefundPaymentHandler(IPaymentService payment) => _payment = payment;

    public async Task Handle(RefundPaymentCommand command, CancellationToken ct)
    {
        await _payment.RefundAsync(command.OrderId, command.Amount, ct);
    }
}
```

---

## Saga Orchestrator Example

The orchestrator executes steps sequentially, compensating completed steps on failure:

```csharp
public class PlaceOrderSagaHandler : IRequestHandler<PlaceOrderSagaCommand, Result<Guid>>
{
    private readonly IValiMediator _mediator;
    private readonly ILogger<PlaceOrderSagaHandler> _logger;

    public PlaceOrderSagaHandler(IValiMediator mediator, ILogger<PlaceOrderSagaHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(PlaceOrderSagaCommand command, CancellationToken ct)
    {
        // Track completed steps for compensation
        var compensableSteps = new Stack<ICompensable>();

        try
        {
            // Step 1: Reserve inventory
            var reserveCmd = new ReserveInventoryCommand(command.OrderId, command.Items);
            var reservation = await _mediator.Send(reserveCmd, ct);
            if (reservation.IsFailure)
                return Result<Guid>.Fail(reservation.Error!, reservation.ErrorType);

            compensableSteps.Push(reserveCmd);

            // Step 2: Charge payment
            var chargeCmd = new ChargePaymentCommand(
                command.OrderId, command.TotalAmount, command.CardToken);
            var payment = await _mediator.Send(chargeCmd, ct);
            if (payment.IsFailure)
            {
                await CompensateAsync(compensableSteps, ct);
                return Result<Guid>.Fail(payment.Error!, payment.ErrorType);
            }

            compensableSteps.Push(chargeCmd);

            // Step 3: Create shipment
            var shipResult = await _mediator.Send(
                new CreateShipmentCommand(command.OrderId, command.ShippingAddress), ct);
            if (shipResult.IsFailure)
            {
                await CompensateAsync(compensableSteps, ct);
                return Result<Guid>.Fail(shipResult.Error!, shipResult.ErrorType);
            }

            _logger.LogInformation("Order {OrderId} placed successfully.", command.OrderId);
            return command.OrderId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error placing order {OrderId}.", command.OrderId);
            await CompensateAsync(compensableSteps, ct);
            return Result<Guid>.Fail("An unexpected error occurred.", ErrorType.Failure);
        }
    }

    private async Task CompensateAsync(Stack<ICompensable> steps, CancellationToken ct)
    {
        // Compensate in reverse order (LIFO)
        while (steps.TryPop(out var step))
        {
            try
            {
                _logger.LogInformation(
                    "Compensating step {Step}.", step.GetType().Name);
                await step.Compensate(_mediator, ct);
            }
            catch (Exception ex)
            {
                // Log but continue compensating remaining steps
                _logger.LogError(ex,
                    "Compensation failed for {Step}. Manual intervention may be required.",
                    step.GetType().Name);
            }
        }
    }
}
```

---

## Key Design Points

**Compensation is dispatched via the mediator.** `Compensable.Compensate()` calls `mediator.Send(IFireAndForget)`, which means compensation commands go through the dispatch pipeline (behaviors and processors).

**Compensate in reverse order.** Use a `Stack<ICompensable>` to track completed steps and pop them LIFO to compensate in reverse execution order.

**Idempotency matters.** Compensation handlers should be idempotent — safe to run multiple times if there is a network failure between the compensation dispatch and its completion.

**No compensation for the failed step.** Only compensate steps that **completed successfully**. The failed step itself did not commit, so it does not need to be undone.

---

## When to Use Compensation

Use the compensation pattern when:

- You have **multiple operations** that must succeed or all roll back
- Operations span **multiple services or systems** (no distributed transaction available)
- Operations have **well-defined inverse actions** (charge → refund, reserve → release, create → delete)

Do not use compensation for:
- Single atomic database transactions (use a regular `try/catch` with transaction rollback)
- Operations without a meaningful inverse (e.g., "send notification" — there is no unsend)

---

## Next Steps

- **[Fire and Forget](06-fire-and-forget.md)** — `IFireAndForget` commands used as compensation actions
- **[Requests](04-requests.md)** — Compensable requests using `IRequest<T>`
- **[Result](10-result.md)** — Handle failures that trigger compensation
