# Vali-Mediator.Caching

Caching integration for the [Vali-Mediator](https://github.com/UBF21/Vali-Mediator) ecosystem.

Provides a pluggable `ICacheStore` abstraction, an in-memory implementation with absolute/sliding
expiry and group-based invalidation, two pipeline behaviors that wire everything together, and
automatic coalescing of concurrent cache misses so a stampede of identical requests only runs the
handler once.

## Installation

```bash
dotnet add package Vali-Mediator.Caching
```

## Quick Start

```csharp
// Program.cs / Startup.cs
builder.Services.AddInMemoryCacheStore(); // or AddCacheStore<MyRedisStore>()

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddCachingBehavior();
});
```

## Making a Query Cacheable

Implement `ICacheable` on your `IRequest<TResponse>`:

```csharp
public sealed class GetProductQuery : IRequest<ProductDto>, ICacheable
{
    public int ProductId { get; init; }

    public string CacheKey => $"product:{ProductId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(10);
    public TimeSpan? SlidingExpiration => null;
    public string? CacheGroup => "products";
    public bool BypassCache => false;
    public CacheOrder Order => CacheOrder.ReadThenWrite;
}
```

## Invalidating Cache From a Command

Implement `IInvalidatesCache` on the command that modifies the resource:

```csharp
public sealed class UpdateProductCommand : IRequest<Unit>, IInvalidatesCache
{
    public int ProductId { get; init; }

    public IReadOnlyList<string> InvalidatedKeys => new List<string> { $"product:{ProductId}" };
    public IReadOnlyList<string> InvalidatedGroups => new List<string>();
}
```

Or invalidate an entire group at once:

```csharp
public sealed class DeleteAllProductsCommand : IRequest<Unit>, IInvalidatesCache
{
    public IReadOnlyList<string> InvalidatedKeys => new List<string>();
    public IReadOnlyList<string> InvalidatedGroups => new List<string> { "products" };
}
```

## CacheOrder

| Value | Behavior |
|-------|----------|
| `ReadThenWrite` (default) | Read cache first; on miss execute handler and write result |
| `WriteOnly` | Always execute handler; skip read but always write |
| `ReadOnly` | Read cache if available; never write after handler |

## Custom Cache Store

Implement `ICacheStore` (and optionally `IGroupAwareCacheStore` for group support):

```csharp
public sealed class RedisCacheStore : ICacheStore
{
    // ... implement TryGetAsync, SetAsync, RemoveAsync, RemoveByGroupAsync
}

// Registration
builder.Services.AddCacheStore<RedisCacheStore>();
```

## In-Memory Store Options

Every limit is validated (must be greater than zero) and bounds memory even when keys/groups come from
client-controlled input — an oversized key or a group past the limit is dropped instead of accepted:

```csharp
builder.Services.AddInMemoryCacheStore(options =>
{
    options.MaxEntries = 5000;               // least-recently-used entry evicted when full (default 10 000)
    options.MaxKeyLength = 512;              // longer keys/group names are never stored (default 512)
    options.MaxGroups = 10_000;              // distinct groups indexed at once (default 10 000)
    options.MaxKeysPerGroup = 10_000;        // keys indexed under one group (default 10 000)
    options.CleanupInterval = TimeSpan.FromMinutes(10);
});
```

## Coalescing Concurrent Misses

When several requests ask for the same cache key at the same time and it's a miss, only the first one
runs the handler — the rest await that same in-flight call instead of each hitting the backing store
(and, transitively, the database). This is automatic whenever `AddCachingBehavior()` is registered; the
only knob is how long a follower waits before giving up and running the handler itself:

```csharp
builder.Services.AddCachingOptions(options =>
{
    options.CoalescingWaitTimeout = TimeSpan.FromSeconds(30); // bounds the damage of a hung handler
});
```

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
