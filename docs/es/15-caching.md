# Caching

`Vali-Mediator.Caching` es un paquete opcional que integra una capa de caché en el pipeline de Vali-Mediator. Las peticiones declaran sus requisitos de caché mediante interfaces; el behavior se encarga de leer, escribir e invalidar de forma transparente.

---

## Instalación

```bash
dotnet add package Vali-Mediator.Caching
```

---

## Registro en DI

Registra el behavior y el store de caché dentro de `Program.cs`:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddCachingBehavior();
});

// Store en memoria incluido en el paquete
builder.Services.AddInMemoryCacheStore();
```

`AddCachingBehavior()` registra `CachingPipelineBehavior<,>` como behavior del pipeline de peticiones. `AddInMemoryCacheStore()` registra la implementación `IMemoryCache`-backed de `ICacheStore`.

---

## Interfaz ICacheable

Implementa `ICacheable` en una petición para que el behavior la gestione automáticamente en caché.

```csharp
public interface ICacheable
{
    /// <summary>Clave única que identifica la entrada en caché.</summary>
    string CacheKey { get; }

    /// <summary>Tiempo de vida absoluto. Null = sin expiración absoluta.</summary>
    TimeSpan? AbsoluteExpiration { get; }

    /// <summary>Tiempo de vida deslizante. Null = sin expiración deslizante.</summary>
    TimeSpan? SlidingExpiration { get; }

    /// <summary>
    /// Grupo lógico al que pertenece la entrada.
    /// Permite invalidar todas las entradas del grupo con una sola llamada.
    /// Null = no pertenece a ningún grupo.
    /// </summary>
    string? CacheGroup { get; }

    /// <summary>
    /// Cuando es true, omite la lectura de caché y siempre llama al handler.
    /// La respuesta obtenida se escribe igualmente en caché.
    /// </summary>
    bool BypassCache { get; }

    /// <summary>Controla el modo de operación del behavior.</summary>
    CacheOrder Order { get; }
}
```

### CacheOrder

```csharp
public enum CacheOrder
{
    /// <summary>Lee de caché si existe; si no, llama al handler y escribe el resultado.</summary>
    ReadThenWrite,

    /// <summary>Solo lee de caché. Si no existe, llama al handler pero no escribe el resultado.</summary>
    ReadOnly,

    /// <summary>Siempre llama al handler y escribe el resultado en caché. No lee.</summary>
    WriteOnly
}
```

### Ejemplo de consulta con ICacheable

```csharp
public class GetProductQuery : IRequest<Result<ProductDto>>, ICacheable
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

El handler no necesita ningún cambio:

```csharp
public class GetProductQueryHandler : IRequestHandler<GetProductQuery, Result<ProductDto>>
{
    private readonly IProductRepository _repository;

    public GetProductQueryHandler(IProductRepository repository)
        => _repository = repository;

    public async Task<Result<ProductDto>> Handle(
        GetProductQuery query, CancellationToken ct)
    {
        var product = await _repository.GetByIdAsync(query.ProductId, ct);
        if (product is null)
            return Result<ProductDto>.Fail("Producto no encontrado.", ErrorType.NotFound);

        return Result<ProductDto>.Ok(product.ToDto());
    }
}
```

---

## Interfaz IInvalidatesCache

Implementa `IInvalidatesCache` en un comando para que el behavior elimine entradas de caché cuando el comando se ejecute con éxito.

```csharp
public interface IInvalidatesCache
{
    /// <summary>
    /// Claves individuales a eliminar de la caché.
    /// Puede ser un array vacío si solo se invalidan grupos.
    /// </summary>
    IReadOnlyList<string> InvalidatedKeys { get; }

    /// <summary>
    /// Grupos lógicos cuyas entradas deben eliminarse completamente.
    /// Puede ser un array vacío si solo se invalidan claves individuales.
    /// </summary>
    IReadOnlyList<string> InvalidatedGroups { get; }
}
```

### Ejemplo de comando con IInvalidatesCache

```csharp
public class UpdateProductCommand : IRequest<Result>, IInvalidatesCache
{
    public int ProductId { get; init; }
    public string Name { get; init; }
    public decimal Price { get; init; }

    // Invalida la entrada específica del producto
    public IReadOnlyList<string> InvalidatedKeys
        => new[] { $"product:{ProductId}" };

    // Invalida también el grupo completo de productos (p.ej. listados paginados)
    public IReadOnlyList<string> InvalidatedGroups
        => new[] { "products" };
}
```

```csharp
public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result>
{
    private readonly IProductRepository _repository;

    public UpdateProductCommandHandler(IProductRepository repository)
        => _repository = repository;

    public async Task<Result> Handle(
        UpdateProductCommand command, CancellationToken ct)
    {
        var product = await _repository.GetByIdAsync(command.ProductId, ct);
        if (product is null)
            return Result.Fail("Producto no encontrado.", ErrorType.NotFound);

        product.UpdateDetails(command.Name, command.Price);
        await _repository.SaveAsync(ct);

        return Result.Ok();
    }
}
```

La invalidación ocurre después de que el handler completa con éxito. Si el handler devuelve un `Result` fallido, la caché no se modifica.

---

## Abstracción ICacheStore

`ICacheStore` es la interfaz que separa el behavior del mecanismo de almacenamiento concreto.

```csharp
public interface ICacheStore
{
    /// <summary>
    /// Intenta obtener una entrada de la caché.
    /// Devuelve (true, valor) si existe; (false, default) si no.
    /// </summary>
    Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct);

    /// <summary>Escribe una entrada en la caché con las opciones de expiración indicadas.</summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpiration,
        TimeSpan? slidingExpiration,
        string? group,
        CancellationToken ct);

    /// <summary>Elimina una entrada por su clave.</summary>
    Task RemoveAsync(string key, CancellationToken ct);

    /// <summary>Elimina todas las entradas que pertenecen al grupo indicado.</summary>
    Task RemoveByGroupAsync(string group, CancellationToken ct);
}
```

### Store en Memoria

El store incluido usa `IMemoryCache` internamente:

```csharp
// Registro con opciones por defecto
builder.Services.AddInMemoryCacheStore();

// Registro con opciones personalizadas
builder.Services.AddInMemoryCacheStore(opts =>
{
    opts.MaxSize = 5000;                              // Número máximo de entradas
    opts.CleanupInterval = TimeSpan.FromMinutes(5);  // Frecuencia de limpieza de entradas expiradas
});
```

#### InMemoryCacheOptions

| Propiedad | Tipo | Valor por defecto | Descripción |
|---|---|---|---|
| `MaxSize` | `long` | `10_000` | Número máximo de entradas en la caché |
| `CleanupInterval` | `TimeSpan` | `2 minutos` | Frecuencia del proceso de limpieza de entradas expiradas |

---

## Store Personalizado

Implementa `ICacheStore` para usar cualquier backend de caché (Redis, NCache, etc.) y regístralo con `AddCacheStore<T>()`:

```csharp
public class MyRedisStore : ICacheStore
{
    private readonly IConnectionMultiplexer _redis;

    public MyRedisStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (!value.HasValue)
            return (false, default);

        return (true, JsonSerializer.Deserialize<T>(value!));
    }

    public async Task SetAsync<T>(
        string key, T value,
        TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration,
        string? group, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, serialized, absoluteExpiration);

        if (group is not null)
        {
            await db.SetAddAsync($"group:{group}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }

    public async Task RemoveByGroupAsync(string group, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync($"group:{group}");
        if (members.Length == 0) return;

        var keys = members.Select(m => (RedisKey)(string)m!).ToArray();
        await db.KeyDeleteAsync(keys);
        await db.KeyDeleteAsync($"group:{group}");
    }
}
```

Registro del store personalizado:

```csharp
// Sustituye AddInMemoryCacheStore() por AddCacheStore<T>()
builder.Services.AddCacheStore<MyRedisStore>();
```

`AddCacheStore<T>()` registra `T` como `ICacheStore` con vida `Singleton`.

---

## Ejemplo Completo

### Petición y comando

```csharp
// Query — usa caché
public class GetOrderQuery : IRequest<Result<OrderDto>>, ICacheable
{
    public Guid OrderId { get; init; }

    public string CacheKey => $"order:{OrderId}";
    public TimeSpan? AbsoluteExpiration => TimeSpan.FromMinutes(5);
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(1);
    public string? CacheGroup => "orders";
    public bool BypassCache => false;
    public CacheOrder Order => CacheOrder.ReadThenWrite;
}

// Command — invalida caché
public class CancelOrderCommand : IRequest<Result>, IInvalidatesCache
{
    public Guid OrderId { get; init; }

    public IReadOnlyList<string> InvalidatedKeys
        => new[] { $"order:{OrderId}" };

    public IReadOnlyList<string> InvalidatedGroups
        => new[] { "orders" };
}
```

### Program.cs completo

```csharp
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Caching;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Caché en memoria con opciones personalizadas
builder.Services.AddInMemoryCacheStore(opts =>
{
    opts.MaxSize = 20_000;
    opts.CleanupInterval = TimeSpan.FromMinutes(3);
});

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // El behavior de caché debe registrarse antes que otros behaviors
    // para que sea la capa más externa del pipeline
    config.AddCachingBehavior();

    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

---

## Comportamiento del Behavior

| Escenario | Acción del behavior |
|---|---|
| `ICacheable`, `Order = ReadThenWrite`, entrada en caché | Devuelve el valor cacheado. No llama al handler. |
| `ICacheable`, `Order = ReadThenWrite`, entrada ausente | Llama al handler y escribe el resultado en caché. |
| `ICacheable`, `Order = ReadOnly`, entrada en caché | Devuelve el valor cacheado. No llama al handler. |
| `ICacheable`, `Order = ReadOnly`, entrada ausente | Llama al handler. No escribe en caché. |
| `ICacheable`, `Order = WriteOnly` | Siempre llama al handler y escribe el resultado. No lee de caché. |
| `ICacheable`, `BypassCache = true` | Llama al handler y escribe el resultado. Ignora cualquier valor existente. |
| `IInvalidatesCache`, handler exitoso | Elimina las claves y grupos declarados tras la ejecución. |
| `IInvalidatesCache`, handler fallido | No modifica la caché. |
| Petición sin `ICacheable` ni `IInvalidatesCache` | El behavior pasa la petición al siguiente sin ninguna operación de caché. |

---

## Siguientes Pasos

- **[Resiliencia](14-resiliencia.md)** — Retry, circuit breaker, timeout y otras políticas de resiliencia
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Behaviors personalizados en el pipeline
- **[Inyección de Dependencias](12-inyeccion-dependencias.md)** — Referencia completa de registro
