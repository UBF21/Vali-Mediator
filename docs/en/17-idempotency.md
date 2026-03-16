# Idempotency

`Vali-Mediator.Idempotency` prevents duplicate execution of commands by storing the result of the first call and returning it on subsequent calls with the same idempotency key. The handler is never invoked more than once per unique key within the configured expiration window.

---

## Installation

```bash
dotnet add package Vali-Mediator.Idempotency
```

---

## DI Setup

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Idempotency;

// Register the in-memory store
builder.Services.AddInMemoryIdempotencyStore();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Idempotency behavior — recommended as the outermost behavior
    config.AddIdempotencyBehavior();
});
```

`AddInMemoryIdempotencyStore()` registers a thread-safe, in-process `IIdempotencyStore` backed by a `ConcurrentDictionary`. Suitable for single-instance deployments and testing. For distributed systems, replace it with a Redis or database-backed store (see [Custom Store](#custom-store) below).

`AddIdempotencyBehavior()` registers `IdempotencyBehavior<,>` for `IRequest<TResponse>` handlers. Only requests that implement `IIdempotent` are intercepted — all other requests pass through without any overhead.

---

## IIdempotent Interface

A request opts into idempotency by implementing `IIdempotent`:

```csharp
using Vali_Mediator.Idempotency;

public interface IIdempotent
{
    string IdempotencyKey { get; }
    TimeSpan? Expiration { get; }
}
```

| Property | Description |
|---|---|
| `IdempotencyKey` | A string that uniquely identifies this specific operation. If two requests share the same key, the second returns the stored result without calling the handler. |
| `Expiration` | How long the stored result is retained. `null` means the entry persists until the store is cleared or the application restarts. |

---

## How It Works

1. A request implementing `IIdempotent` enters the pipeline.
2. The `IdempotencyBehavior` queries the store for an entry matching `IdempotencyKey`.
3. **If an entry exists and has not expired:** the stored result is deserialized and returned immediately. The handler is not called.
4. **If no entry exists:** the handler (and all inner behaviors) execute normally. The response is serialized and stored before being returned to the caller.
5. Subsequent calls with the same key within the expiration window receive the cached response.

The behavior is transparent to the handler — handlers do not need to be aware of idempotency.

---

## IIdempotencyStore

The store abstraction that all idempotency behavior depends on:

```csharp
using Vali_Mediator.Idempotency.Store;

public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct);
    Task StoreAsync(IdempotencyEntry entry, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
}
```

| Method | Description |
|---|---|
| `FindAsync` | Returns the stored entry for the given key, or `null` if not found or expired |
| `StoreAsync` | Persists a new entry. Implementations should respect `ExpiresAt` for eviction |
| `RemoveAsync` | Explicitly deletes an entry. Useful for rollback or administrative operations |
| `ExistsAsync` | Returns `true` if a non-expired entry exists for the given key |

### Custom Store

Replace the in-memory store with any persistent or distributed backend:

```csharp
using Vali_Mediator.Idempotency.Store;
using StackExchange.Redis;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDatabase _db;

    public RedisIdempotencyStore(IConnectionMultiplexer redis)
        => _db = redis.GetDatabase();

    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct)
    {
        var value = await _db.StringGetAsync(key).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<IdempotencyEntry>(value!);
    }

    public async Task StoreAsync(IdempotencyEntry entry, CancellationToken ct)
    {
        var expiry = entry.ExpiresAt.HasValue
            ? entry.ExpiresAt.Value - DateTimeOffset.UtcNow
            : (TimeSpan?)null;

        var serialized = JsonSerializer.Serialize(entry);
        await _db.StringSetAsync(entry.Key, serialized, expiry).ConfigureAwait(false);
    }

    public Task RemoveAsync(string key, CancellationToken ct)
        => _db.KeyDeleteAsync(key).AsTask();

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
        => await _db.KeyExistsAsync(key).ConfigureAwait(false);
}

// Registration — replaces AddInMemoryIdempotencyStore()
builder.Services.AddIdempotencyStore<RedisIdempotencyStore>();
```

`AddIdempotencyStore<T>()` registers your implementation as the singleton `IIdempotencyStore`.

---

## IdempotencyEntry

The model persisted in the store:

```csharp
using Vali_Mediator.Idempotency.Store;

public sealed class IdempotencyEntry
{
    public string Key { get; init; }
    public string SerializedResponse { get; init; }
    public string ResponseType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

| Property | Description |
|---|---|
| `Key` | The idempotency key from `IIdempotent.IdempotencyKey` |
| `SerializedResponse` | The handler response serialized as a JSON string |
| `ResponseType` | Assembly-qualified name of the response type, used for deserialization |
| `ExpiresAt` | Absolute expiry timestamp, computed from `Expiration` at the time of the first call. `null` if no expiration was specified. |
| `CreatedAt` | UTC timestamp of when the entry was first stored |

---

## IIdempotencySerializer

Responses are serialized to and deserialized from `string` before being stored:

```csharp
using Vali_Mediator.Idempotency.Serialization;

public interface IIdempotencySerializer
{
    string Serialize(object response, Type responseType);
    object? Deserialize(string serialized, Type responseType);
}
```

The default implementation is `JsonIdempotencySerializer`, which uses `System.Text.Json` with default options.

### Custom Serializer

```csharp
using Vali_Mediator.Idempotency.Serialization;

public class NewtonsoftIdempotencySerializer : IIdempotencySerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        NullValueHandling = NullValueHandling.Ignore
    };

    public string Serialize(object response, Type responseType)
        => JsonConvert.SerializeObject(response, responseType, Settings);

    public object? Deserialize(string serialized, Type responseType)
        => JsonConvert.DeserializeObject(serialized, responseType, Settings);
}

// Registration — replaces JsonIdempotencySerializer
builder.Services.AddIdempotencySerializer<NewtonsoftIdempotencySerializer>();
```

---

## Full Example: PlaceOrderCommand

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator.Idempotency;

// Command implements IIdempotent
public sealed class PlaceOrderCommand : IRequest<Result<Guid>>, IIdempotent
{
    public Guid CustomerId { get; init; }
    public IReadOnlyList<OrderLineDto> Lines { get; init; }

    // Key encodes all parameters that define a unique operation.
    // This prevents both accidental duplicates and replay attacks
    // targeting a different customer or a different set of lines.
    public string IdempotencyKey =>
        $"order:{CustomerId}:{string.Join(",", Lines.Select(l => $"{l.ProductId}x{l.Quantity}"))}";

    // Cached for 24 hours — allows clients to safely retry within that window
    public TimeSpan? Expiration => TimeSpan.FromHours(24);
}

// Handler — executes only on the first call per key
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Result<Guid>>
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentService _payments;

    public PlaceOrderCommandHandler(IOrderRepository orders, IPaymentService payments)
    {
        _orders = orders;
        _payments = payments;
    }

    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Lines);

        var paymentResult = await _payments.ChargeAsync(order, ct);
        if (!paymentResult.IsSuccess)
            return Result<Guid>.Fail(paymentResult.Error!, ErrorType.Failure);

        await _orders.SaveAsync(order, ct);

        return Result<Guid>.Ok(order.Id);
    }
}
```

Usage in a controller or endpoint:

```csharp
[HttpPost("orders")]
public async Task<IActionResult> PlaceOrder(PlaceOrderCommand command, CancellationToken ct)
{
    // If a request with the same IdempotencyKey was already processed,
    // the handler is not called again — the stored Result<Guid> is returned directly.
    var result = await _mediator.Send(command, ct);
    return result.ToActionResult(this);
}
```

---

## When to Use Idempotency

Idempotency is appropriate for operations where executing the handler more than once would cause incorrect or harmful side effects:

| Scenario | Why idempotency matters |
|---|---|
| Payment processing | Charging a customer twice for a single intent is a critical error |
| Order placement | Duplicate orders cause fulfillment and inventory problems |
| API retries | Clients retry on network timeout without knowing if the first attempt succeeded |
| Webhook delivery | Webhook providers often deliver the same event more than once |
| Distributed sagas | Compensating steps may be retried by the orchestrator |

---

## Idempotency Key Design

> The idempotency key must encode all parameters that define a unique, meaningful operation. A bare GUID from the client is insufficient on its own.

### Wrong approach

```csharp
// A GUID generated by the client is not enough:
// two different orders could share the same GUID by accident,
// or the client could send the same GUID for a legitimately different request.
public string IdempotencyKey => _clientProvidedGuid.ToString();
```

### Correct approach

Combine the client-provided token with server-known context to produce a key that is specific to the operation:

```csharp
// Good: key includes the customer, the client token, and the operation type.
// Two requests with the same GUID but a different CustomerId produce different keys.
public string IdempotencyKey =>
    $"place-order:{CustomerId}:{_clientIdempotencyToken}";

// Good: key is derived entirely from the operation's data.
// No client token needed when the data itself is deterministic.
public string IdempotencyKey =>
    $"payment:{CustomerId}:{InvoiceId}:{AmountCents}";
```

---

## Complete Program.cs Example

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Idempotency;

var builder = WebApplication.CreateBuilder(args);

// Redis connection for distributed idempotency
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

// Idempotency — use Redis store in production
builder.Services.AddIdempotencyStore<RedisIdempotencyStore>();

// Keep the default JsonIdempotencySerializer, or swap it:
// builder.Services.AddIdempotencySerializer<NewtonsoftIdempotencySerializer>();

// Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Idempotency as the outermost behavior — short-circuits before any other behavior runs
    config.AddIdempotencyBehavior();

    // Other behaviors run only when the handler actually executes
    config.AddRequestBehavior<ValidationBehavior<,>>();
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Summary

| Component | Interface / Type | Default |
|---|---|---|
| Opt-in marker | `IIdempotent` | — |
| Store abstraction | `IIdempotencyStore` | `InMemoryIdempotencyStore` |
| Serializer abstraction | `IIdempotencySerializer` | `JsonIdempotencySerializer` |
| Stored model | `IdempotencyEntry` | — |
| Pipeline behavior | `IdempotencyBehavior<,>` | Registered via `AddIdempotencyBehavior()` |

| Registration method | Effect |
|---|---|
| `services.AddInMemoryIdempotencyStore()` | Registers thread-safe in-process store |
| `services.AddIdempotencyStore<T>()` | Replaces the store with a custom implementation |
| `services.AddIdempotencySerializer<T>()` | Replaces `JsonIdempotencySerializer` |
| `config.AddIdempotencyBehavior()` | Registers pipeline behavior for `IRequest<TResponse>` |

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Understand behavior registration order and short-circuiting
- **[Result](10-result.md)** — Idempotency stores and returns `Result<T>` transparently
- **[Dependency Injection](12-dependency-injection.md)** — Full registration reference
