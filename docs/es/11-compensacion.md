# Compensación

La compensación habilita el patrón Saga: cuando falla un paso en una operación de múltiples pasos, los pasos ya completados se deshacen ejecutando sus acciones de compensación.

---

## El Problema: Transacciones Distribuidas

En una arquitectura de microservicios o multi-servicio, no existe una única transacción de base de datos que abarque todas las operaciones. Si el paso 3 de un proceso de 5 pasos falla, los pasos 1 y 2 deben deshacerse explícitamente:

```
1. Reservar inventario    ✓
2. Cobrar pago            ✓
3. Crear envío            ✗  ← falla aquí
   → Compensar paso 2: Reembolsar pago
   → Compensar paso 1: Liberar inventario
```

El modelo de compensación de Vali-Mediator soporta este patrón mediante `ICompensable` y la clase base `Compensable`.

---

## ICompensable

```csharp
public interface ICompensable
{
    // Devuelve la acción de compensación, o null si no se necesita compensación
    IFireAndForget? GetCompensation();

    // Despacha la acción de compensación a través del mediator
    Task Compensate(IValiMediator mediator, CancellationToken cancellationToken = default);
}
```

La acción de compensación es un comando `IFireAndForget` que el mediator despacha cuando se invoca.

---

## Clase Base Compensable

`Compensable` provee la implementación de `Compensate` — solo necesitas sobrescribir `GetCompensation`:

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

## Definiendo Comandos Compensables

Haz que una petición implemente `Compensable` y devuelva su rollback como un `IFireAndForget`:

```csharp
// Comandos de rollback fire-and-forget
public record ReleaseInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItem> Items) : IFireAndForget;

public record RefundPaymentCommand(
    Guid OrderId,
    decimal Amount,
    string TransactionId) : IFireAndForget;

// Paso 1: Reservar inventario (compensable)
public record ReserveInventoryCommand(
    Guid OrderId,
    IReadOnlyList<OrderItem> Items)
    : Compensable, IRequest<Result<InventoryReservation>>
{
    public override IFireAndForget? GetCompensation()
        => new ReleaseInventoryCommand(OrderId, Items);
}

// Paso 2: Cobrar pago (compensable)
public record ChargePaymentCommand(
    Guid OrderId,
    decimal Amount,
    string CardToken)
    : Compensable, IRequest<Result<PaymentTransaction>>
{
    public override IFireAndForget? GetCompensation()
        => new RefundPaymentCommand(OrderId, Amount, string.Empty);
        // Nota: el TransactionId no está disponible en el momento de la construcción.
        // En escenarios reales, guarda el ID de transacción después de que el paso 2 sea exitoso
        // y úsalo en la compensación.
}
```

---

## Handlers de Compensación

Cada comando de rollback necesita un handler:

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
        _logger.LogInformation("Liberando inventario para el pedido {OrderId}.", command.OrderId);
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

## Ejemplo de Orquestador Saga

El orquestador ejecuta pasos secuencialmente, compensando los pasos completados en caso de fallo:

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
        // Rastrear los pasos completados para compensación
        var compensableSteps = new Stack<ICompensable>();

        try
        {
            // Paso 1: Reservar inventario
            var reserveCmd = new ReserveInventoryCommand(command.OrderId, command.Items);
            var reservation = await _mediator.Send(reserveCmd, ct);
            if (reservation.IsFailure)
                return Result<Guid>.Fail(reservation.Error!, reservation.ErrorType);

            compensableSteps.Push(reserveCmd);

            // Paso 2: Cobrar pago
            var chargeCmd = new ChargePaymentCommand(
                command.OrderId, command.TotalAmount, command.CardToken);
            var payment = await _mediator.Send(chargeCmd, ct);
            if (payment.IsFailure)
            {
                await CompensateAsync(compensableSteps, ct);
                return Result<Guid>.Fail(payment.Error!, payment.ErrorType);
            }

            compensableSteps.Push(chargeCmd);

            // Paso 3: Crear envío
            var shipResult = await _mediator.Send(
                new CreateShipmentCommand(command.OrderId, command.ShippingAddress), ct);
            if (shipResult.IsFailure)
            {
                await CompensateAsync(compensableSteps, ct);
                return Result<Guid>.Fail(shipResult.Error!, shipResult.ErrorType);
            }

            _logger.LogInformation("Pedido {OrderId} creado exitosamente.", command.OrderId);
            return command.OrderId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear el pedido {OrderId}.", command.OrderId);
            await CompensateAsync(compensableSteps, ct);
            return Result<Guid>.Fail("Ocurrió un error inesperado.", ErrorType.Failure);
        }
    }

    private async Task CompensateAsync(Stack<ICompensable> steps, CancellationToken ct)
    {
        // Compensar en orden inverso (LIFO)
        while (steps.TryPop(out var step))
        {
            try
            {
                _logger.LogInformation(
                    "Compensando paso {Step}.", step.GetType().Name);
                await step.Compensate(_mediator, ct);
            }
            catch (Exception ex)
            {
                // Registrar pero continuar compensando los pasos restantes
                _logger.LogError(ex,
                    "La compensación falló para {Step}. Puede requerirse intervención manual.",
                    step.GetType().Name);
            }
        }
    }
}
```

---

## Puntos Clave de Diseño

**La compensación se despacha a través del mediator.** `Compensable.Compensate()` llama a `mediator.Send(IFireAndForget)`, lo que significa que los comandos de compensación pasan por el pipeline de dispatch (behaviors y processors).

**Compensar en orden inverso.** Usa un `Stack<ICompensable>` para rastrear los pasos completados y sácalos en LIFO para compensar en orden inverso de ejecución.

**La idempotencia es importante.** Los handlers de compensación deben ser idempotentes — seguros de ejecutar múltiples veces si hay un fallo de red entre el despacho de la compensación y su completación.

**No compensar el paso fallido.** Solo compensar pasos que **se completaron exitosamente**. El paso fallido en sí no confirmó, por lo que no necesita deshacerse.

---

## Cuándo Usar Compensación

Usa el patrón de compensación cuando:

- Tienes **múltiples operaciones** que deben tener éxito o todas deshacerse
- Las operaciones abarcan **múltiples servicios o sistemas** (sin transacción distribuida disponible)
- Las operaciones tienen **acciones inversas bien definidas** (cobrar → reembolsar, reservar → liberar, crear → eliminar)

No uses compensación para:
- Transacciones atómicas únicas de base de datos (usa un `try/catch` regular con rollback de transacción)
- Operaciones sin una inversa significativa (por ejemplo, "enviar notificación" — no existe un "des-enviar")

---

## Siguientes Pasos

- **[Fire and Forget](06-fire-and-forget.md)** — Comandos `IFireAndForget` usados como acciones de compensación
- **[Peticiones](04-peticiones.md)** — Peticiones compensables usando `IRequest<T>`
- **[Result](10-resultado.md)** — Manejar fallos que desencadenan compensación
