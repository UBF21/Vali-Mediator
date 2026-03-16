# Caching

`Vali-Mediator.Caching` is an optional package that adds transparent response caching to request handlers via the pipeline. Handlers declare their caching requirements by implementing `ICacheable`; commands that invalidate cached data implement `IInvalidatesCache`.

---

## Installation

```bash
dotnet add package Vali-Mediator.Caching
```

---

## DI Registration

Register the caching behavior inside `AddValiMediator` and choose a cache store implementation. The in-memory store is provided by the package:

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Caching;

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddCachingBehavior();
});

builder.Services.AddInMemoryCacheStore();
```

---

## ICacheable

Implement `ICacheable` on an `IRequest<TResponse>` to opt the handler response into the cache:

```csharp
using Vali_Mediator.Caching;

public record GetProductQuery(int ProductId)
    : IRequest<Result<ProductDto>>, ICacheable
{
    public string CacheKey             => $"product:{ProductId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(10);
    public TimeSpan? SlidingExpiration  => null;
    public string? CacheGroup          => "products";
    public bool BypassCache            => false;
    public CacheOrder Order            => CacheOrder.ReadThenWrite;
}
```

### ICacheable Properties

| Property | Type | Description |
|---|---|---|
| `CacheKey` | `string` | Unique key for this cached entry |
| `AbsoluteExpiration` | `TimeSpan?` | Time from insertion after which the entry expires; `null` means no absolute expiration |
| `SlidingExpiration` | `TimeSpan?` | Time of inactivity after which the entry expires; `null` means no sliding expiration |
| `CacheGroup` | `string?` | Logical group name — used by `IInvalidatesCache.InvalidatedGroups` to flush all entries in the group |
| `BypassCache` | `bool` | When `true`, skips the cache for this specific request instance (useful for forced refreshes) |
| `Order` | `CacheOrder` | Controls read/write behavior (see below) |

### CacheOrder Enum

| Value | Behavior |
|---|---|
| `ReadThenWrite` | Check cache first; on miss, execute handler and store the result |
| `ReadOnly` | Return cached value if present; do not cache the handler result on a miss |
| `WriteOnly` | Always execute the handler and overwrite the cached value; never read from cache |

---

## IInvalidatesCache

Implement `IInvalidatesCache` on a command to evict cache entries after the handler executes successfully:

```csharp
using Vali_Mediator.Caching;

public record UpdateProductCommand(int ProductId, string Name, decimal Price)
    : IRequest<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> InvalidatedKeys   => new[] { $"product:{ProductId}" };
    public IReadOnlyList<string> InvalidatedGroups => new[] { "products" };
}
```

### IInvalidatesCache Properties

| Property | Type | Description |
|---|---|---|
| `InvalidatedKeys` | `IReadOnlyList<string>` | Specific cache keys to remove after a successful handler execution |
| `InvalidatedGroups` | `IReadOnlyList<string>` | Cache groups to flush — removes all entries that declared a matching `CacheGroup` |

Eviction runs after the handler returns and only when the handler completes without throwing. If the handler returns a `Result` or `Result<T>` failure, eviction is skipped.

---

## ICacheStore

`ICacheStore` is the abstraction layer between the caching behavior and the underlying cache implementation:

```csharp
public interface ICacheStore
{
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task RemoveByGroupAsync(string group, CancellationToken ct);
}
```

| Method | Description |
|---|---|
| `TryGetAsync<T>` | Returns the cached value and a `Found` flag. Returns `(false, default)` on a miss |
| `SetAsync<T>` | Stores the value with the specified expiration parameters |
| `RemoveAsync` | Removes a single entry by key |
| `RemoveByGroupAsync` | Removes all entries associated with a group |

---

## Custom Cache Store

Provide your own store (e.g., Redis, Garnet, Hybrid) by implementing `ICacheStore` and registering it:

```csharp
public class MyRedisStore : ICacheStore
{
    private readonly IConnectionMultiplexer _redis;

    public MyRedisStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<(bool Found, T? Value)> TryGetAsync<T>(
        string key, CancellationToken ct)
    {
        var db  = _redis.GetDatabase();
        var raw = await db.StringGetAsync(key);
        if (!raw.HasValue)
            return (false, default);

        var value = JsonSerializer.Deserialize<T>(raw!);
        return (true, value);
    }

    public async Task SetAsync<T>(
        string key, T value,
        TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration,
        CancellationToken ct)
    {
        var db  = _redis.GetDatabase();
        var raw = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, raw, absoluteExpiration);
    }

    public Task RemoveAsync(string key, CancellationToken ct)
        => _redis.GetDatabase().KeyDeleteAsync(key).AsTask();

    public Task RemoveByGroupAsync(string group, CancellationToken ct)
    {
        // Implementation depends on key-set tracking strategy
        throw new NotImplementedException();
    }
}
```

Register the custom store:

```csharp
builder.Services.AddCacheStore<MyRedisStore>();
```

`AddCacheStore<T>()` registers `T` as the `ICacheStore` implementation with a scoped lifetime.

---

## InMemoryCacheOptions

Tune the built-in in-memory store by passing options to `AddInMemoryCacheStore`:

```csharp
builder.Services.AddInMemoryCacheStore(opts =>
{
    opts.MaxSize         = 1000;
    opts.CleanupInterval = TimeSpan.FromMinutes(5);
});
```

| Option | Type | Default | Description |
|---|---|---|---|
| `MaxSize` | `int` | `500` | Maximum number of entries held in memory; oldest entries are evicted when the limit is reached |
| `CleanupInterval` | `TimeSpan` | 2 minutes | Interval at which expired entries are scanned and removed from memory |

---

## Full Example

### Query with ICacheable

```csharp
// Query
public record GetOrderQuery(int OrderId)
    : IRequest<Result<OrderDto>>, ICacheable
{
    public string CacheKey              => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration  => null;
    public string? CacheGroup           => "orders";
    public bool BypassCache             => false;
    public CacheOrder Order             => CacheOrder.ReadThenWrite;
}

// Handler — the caching behavior intercepts before this runs
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, Result<OrderDto>>
{
    private readonly IOrderRepository _orders;

    public GetOrderQueryHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(query.OrderId, ct);
        if (order is null)
            return Result<OrderDto>.Fail("Order not found.", ErrorType.NotFound);

        return Result<OrderDto>.Ok(order.ToDto());
    }
}
```

### Command with IInvalidatesCache

```csharp
// Command
public record CancelOrderCommand(int OrderId)
    : IRequest<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> InvalidatedKeys   => new[] { $"order:{OrderId}" };
    public IReadOnlyList<string> InvalidatedGroups => new[] { "orders" };
}

// Handler
public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IOrderRepository _orders;

    public CancelOrderCommandHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(command.OrderId, ct);
        if (order is null)
            return Result.Fail("Order not found.", ErrorType.NotFound);

        order.Cancel();
        await _orders.SaveAsync(ct);

        // Cache eviction of "order:{OrderId}" and the "orders" group
        // is handled automatically by the caching behavior after this returns.
        return Result.Ok();
    }
}
```

### Force Cache Bypass

Set `BypassCache = true` from the call site to skip the cache for a single request instance:

```csharp
var query = new GetOrderQuery(orderId) with { BypassCache = true };
var result = await mediator.Send(query, ct);
```

Because `IRequest` records support `with` expressions, no additional infrastructure is needed.

---

## Program.cs: Complete Setup

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Caching;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddCachingBehavior();
});

// Choose one:
builder.Services.AddInMemoryCacheStore(opts =>
{
    opts.MaxSize         = 2000;
    opts.CleanupInterval = TimeSpan.FromMinutes(3);
});

// Or a custom store:
// builder.Services.AddCacheStore<MyRedisStore>();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();

app.Run();
```

---

## Caching Behavior Flow

```
Request arrives
      │
      ▼
  ICacheable?
  ├── No  ──────────────────────────────► Handler
  └── Yes
        │
        ▼
    BypassCache || Order == WriteOnly?
    ├── Yes ─────────────────────────► Handler ──► SetAsync (if WriteOnly or ReadThenWrite)
    └── No
          │
          ▼
       TryGetAsync(CacheKey)
       ├── Hit ────────────────────► return cached value
       └── Miss
             │
             ▼
          Order == ReadOnly?
          ├── Yes ────────────────► Handler (result not cached)
          └── No (ReadThenWrite)
                │
                ▼
             Handler ──► SetAsync(CacheKey, result, expiration)
                                │
                                ▼
                        IInvalidatesCache?
                        ├── No  ──► done
                        └── Yes ──► RemoveAsync / RemoveByGroupAsync
```

---

## Next Steps

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — How the caching behavior hooks into the pipeline
- **[Result](10-result.md)** — Using `Result<T>` in cacheable handlers
- **[Resilience](14-resilience.md)** — Combining caching with resilience policies
