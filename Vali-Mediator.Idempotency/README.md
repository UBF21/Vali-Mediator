# Vali-Mediator.Idempotency

Idempotency pipeline integration for [Vali-Mediator](https://github.com/UBF21/Vali-Mediator) (.NET 7 / 8 / 9).

Prevent duplicate handler executions by caching responses keyed on a caller-supplied idempotency key.

---

## Features

- **`IIdempotent`** — marker interface your request implements to opt into idempotency.
- **`IdempotencyBehavior<TRequest,TResponse>`** — open-generic `IPipelineBehavior` that intercepts the pipeline, serves cached responses, and prevents concurrent duplicate executions via per-key `SemaphoreSlim` locking.
- **`IIdempotencyStore`** — pluggable persistence abstraction (find / store / remove / exists).
- **`InMemoryIdempotencyStore`** — thread-safe `ConcurrentDictionary` implementation with auto-eviction of expired entries and a periodic cleanup sweep every 100 writes.
- **`IIdempotencySerializer`** — pluggable serialization abstraction.
- **`JsonIdempotencySerializer`** — default implementation using `System.Text.Json` (no extra package needed).
- Zero external dependencies beyond `Microsoft.Extensions.DependencyInjection.Abstractions` and `Vali-Mediator`.

---

## Installation

```
dotnet add package Vali-Mediator.Idempotency
```

---

## Quick Start

### 1. Mark your request as idempotent

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator_Idempotency.Core.Interfaces;

public class PlaceOrderCommand : IRequest<OrderId>, IIdempotent
{
    // The unique key sent by the caller (e.g. from an HTTP header)
    public string IdempotencyKey { get; init; } = string.Empty;

    // How long to keep the stored response. null = forever.
    public TimeSpan? Expiration { get; init; } = TimeSpan.FromHours(24);

    // ... command properties
    public Guid CustomerId { get; init; }
    public List<OrderLine> Lines { get; init; } = new();
}
```

### 2. Register in DI

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddIdempotencyBehavior(); // registers IdempotencyBehavior<,>
});

// Register the in-memory store + JSON serializer
builder.Services.AddInMemoryIdempotencyStore();
```

### 3. Use from your handler or controller

```csharp
// Controller example
public async Task<IActionResult> PlaceOrder(
    [FromHeader(Name = "Idempotency-Key")] string key,
    PlaceOrderRequest body)
{
    var command = new PlaceOrderCommand
    {
        IdempotencyKey = key,
        Expiration = TimeSpan.FromHours(24),
        CustomerId = body.CustomerId,
        Lines = body.Lines
    };

    var orderId = await _mediator.Send<OrderId>(command);
    return Ok(orderId);
}
```

The second call with the same `Idempotency-Key` within 24 hours returns the cached `OrderId` without re-executing the handler.

---

## Custom Store

Implement `IIdempotencyStore` and register it:

```csharp
public class RedisIdempotencyStore : IIdempotencyStore
{
    // ... implementation backed by Redis / Distributed Cache
}

// Registration
builder.Services.AddIdempotencyStore<RedisIdempotencyStore>();
```

---

## Custom Serializer

Implement `IIdempotencySerializer` and register it:

```csharp
public class MessagePackSerializer : IIdempotencySerializer
{
    public byte[] Serialize<T>(T value) { ... }
    public T? Deserialize<T>(byte[] data) { ... }
}

// Registration (replaces the default JsonIdempotencySerializer)
builder.Services.AddIdempotencySerializer<MessagePackSerializer>();
```

---

## How it works

1. The `IdempotencyBehavior` checks whether the incoming request implements `IIdempotent`.
2. If not, it passes through transparently.
3. If yes, it acquires a per-key `SemaphoreSlim` to serialize concurrent calls with the same key.
4. It queries the store: if a live (non-expired) entry exists, the stored bytes are deserialized and returned immediately — the handler is never called.
5. Otherwise it invokes the handler, serializes the response with `System.Text.Json`, stores the `IdempotencyEntry` (with optional expiry), and returns the response.

---

## API Reference

| Type | Description |
|---|---|
| `IIdempotent` | Marker interface — implement on your `IRequest<TResponse>` |
| `IdempotencyEntry` | Stored envelope: key, serialized bytes, type name, timestamps, expiry |
| `IIdempotencyStore` | Persistence contract: `FindAsync`, `StoreAsync`, `RemoveAsync`, `ExistsAsync` |
| `InMemoryIdempotencyStore` | Built-in thread-safe in-memory store |
| `IIdempotencySerializer` | Serialization contract: `Serialize<T>`, `Deserialize<T>` |
| `JsonIdempotencySerializer` | Default `System.Text.Json` serializer |
| `IdempotencyBehavior<TRequest,TResponse>` | Open-generic pipeline behavior |

### Extension methods

| Method | Target | Description |
|---|---|---|
| `config.AddIdempotencyBehavior()` | `ValiMediatorConfiguration` | Registers `IdempotencyBehavior<,>` |
| `services.AddInMemoryIdempotencyStore()` | `IServiceCollection` | Registers `InMemoryIdempotencyStore` + `JsonIdempotencySerializer` |
| `services.AddIdempotencyStore<TStore>()` | `IServiceCollection` | Registers a custom `IIdempotencyStore` |
| `services.AddIdempotencySerializer<TSerializer>()` | `IServiceCollection` | Replaces the default serializer |

---

## Donations

If Vali-Mediator is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Mediator).
