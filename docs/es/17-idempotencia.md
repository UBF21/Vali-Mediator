# Idempotencia

El paquete `Vali-Mediator.Idempotency` garantiza que un handler se ejecute exactamente una vez para una clave dada, incluso si la misma peticion llega multiples veces. Las ejecuciones duplicadas reciben la respuesta almacenada sin invocar al handler.

---

## Instalacion

```bash
dotnet add package Vali-Mediator.Idempotency
```

---

## Configuracion en el Contenedor de DI

```csharp
// Program.cs
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Idempotency;

var builder = WebApplication.CreateBuilder(args);

// Almacen en memoria (adecuado para desarrollo y escenarios de instancia unica)
builder.Services.AddInMemoryIdempotencyStore();

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // El behavior de idempotencia debe registrarse antes de los behaviors de negocio
    config.AddIdempotencyBehavior();
});
```

---

## Marcar una Peticion como Idempotente

Implementa `IIdempotent` en cualquier peticion que deba ser idempotente:

```csharp
public interface IIdempotent
{
    // Clave unica que identifica esta ejecucion especifica
    string IdempotencyKey { get; }

    // Tiempo de vida de la entrada almacenada; null = sin expiracion
    TimeSpan? Expiration { get; }
}
```

### Ejemplo: PlaceOrderCommand

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.Result;
using Vali_Mediator.Idempotency;

public sealed record PlaceOrderCommand(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderLineDto> Lines)
    : IRequest<Result<string>>, IIdempotent
{
    // La clave incorpora el ID del pedido: mismo pedido = misma clave
    public string IdempotencyKey => $"place-order:{OrderId}";

    // Retener el resultado 24 horas para cubrir reintentos del cliente
    public TimeSpan? Expiration => TimeSpan.FromHours(24);
}
```

---

## Como Funciona

```
Primera llamada  ─► behavior revisa el store ─► clave no existe
                 ─► invoca al handler
                 ─► almacena la respuesta serializada
                 ─► devuelve la respuesta al llamador

Llamadas duplicadas ─► behavior revisa el store ─► clave existe
                    ─► deserializa la respuesta almacenada
                    ─► devuelve la respuesta sin ejecutar el handler
```

El handler nunca se invoca una segunda vez para la misma clave mientras la entrada no haya expirado. Esto es valido incluso si las llamadas duplicadas llegan de forma concurrente: el behavior aplica un lock logico por clave durante la primera ejecucion.

---

## IIdempotencyStore

La abstraccion del almacen permite sustituir el backend sin cambiar la logica del pipeline.

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default);
    Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
```

### IdempotencyEntry

```csharp
public sealed class IdempotencyEntry
{
    // Clave de idempotencia tal como la devuelve IIdempotent.IdempotencyKey
    public string Key { get; init; }

    // Respuesta serializada (JSON por defecto)
    public string SerializedResponse { get; init; }

    // Tipo CLR de la respuesta; necesario para deserializar correctamente
    public Type ResponseType { get; init; }

    // Fecha y hora UTC de expiracion; null significa sin expiracion
    public DateTimeOffset? ExpiresAt { get; init; }

    // Fecha y hora UTC en que se creo la entrada
    public DateTimeOffset CreatedAt { get; init; }
}
```

### Almacen personalizado con Redis

```csharp
using StackExchange.Redis;
using Vali_Mediator.Idempotency;

public class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDatabase _db;
    private readonly IIdempotencySerializer _serializer;

    public RedisIdempotencyStore(IConnectionMultiplexer redis, IIdempotencySerializer serializer)
    {
        _db = redis.GetDatabase();
        _serializer = serializer;
    }

    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct = default)
    {
        var raw = await _db.StringGetAsync(key);
        if (!raw.HasValue)
            return null;

        return _serializer.Deserialize<IdempotencyEntry>(raw!);
    }

    public async Task StoreAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        var serialized = _serializer.Serialize(entry);
        var expiry = entry.ExpiresAt.HasValue
            ? entry.ExpiresAt.Value - DateTimeOffset.UtcNow
            : (TimeSpan?)null;

        await _db.StringSetAsync(entry.Key, serialized, expiry);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(key);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await _db.KeyExistsAsync(key);
}
```

```csharp
// Registro
builder.Services.AddIdempotencyStore<RedisIdempotencyStore>();
```

---

## IIdempotencySerializer

El serializador controla como se convierte la respuesta del handler a texto y viceversa.

```csharp
public interface IIdempotencySerializer
{
    string Serialize<T>(T value);
    T? Deserialize<T>(string serialized);
    object? Deserialize(string serialized, Type type);
}
```

El paquete incluye `JsonIdempotencySerializer` como implementacion por defecto, basada en `System.Text.Json`.

### Serializador personalizado

```csharp
public class NewtonsoftIdempotencySerializer : IIdempotencySerializer
{
    private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        NullValueHandling = NullValueHandling.Ignore
    };

    public string Serialize<T>(T value)
        => JsonConvert.SerializeObject(value, Settings);

    public T? Deserialize<T>(string serialized)
        => JsonConvert.DeserializeObject<T>(serialized, Settings);

    public object? Deserialize(string serialized, Type type)
        => JsonConvert.DeserializeObject(serialized, type, Settings);
}
```

```csharp
// Registro
builder.Services.AddIdempotencySerializer<NewtonsoftIdempotencySerializer>();
```

---

## Cuando Usar Idempotencia

La idempotencia es apropiada cuando:

- **Procesamiento de pagos** — un cargo no debe ejecutarse dos veces si la red reintenta la peticion.
- **Colocacion de pedidos** — el mismo pedido no debe duplicarse por un doble clic o un timeout del cliente.
- **Reintentos de API** — cuando el cliente no puede distinguir si una peticion fallo antes o despues de que el servidor la procesara.
- **Mensajeria at-least-once** — cuando un bus de mensajes puede entregar el mismo mensaje mas de una vez.

No es necesaria para operaciones de solo lectura (queries), ya que ejecutarlas multiples veces no produce efectos secundarios.

---

## Nota Importante: Diseno de la Clave

La clave de idempotencia debe codificar todos los parametros que afectan al resultado del handler. Una clave demasiado amplia puede hacer que peticiones distintas compartan resultado; una clave demasiado restringida puede no detectar duplicados.

```csharp
// Correcto: la clave identifica de forma unica esta transaccion concreta
public string IdempotencyKey => $"payment:{PaymentId}";

// Incorrecto: dos pagos distintos para el mismo cliente tendrian la misma clave
public string IdempotencyKey => $"payment:customer:{CustomerId}";

// Correcto cuando el cliente genera el ID antes de enviar la peticion
public string IdempotencyKey => $"order:{ClientGeneratedOrderId}";
```

Si el cliente no tiene un identificador natural, debe generarlo (e.g. un `Guid`) y enviarlo junto con la peticion. El servidor lo usa como clave sin volver a generarlo.

---

## Configuracion Completa en Program.cs

```csharp
using StackExchange.Redis;
using Vali_Mediator.Core.General.Extension;
using Vali_Mediator.Idempotency;

var builder = WebApplication.CreateBuilder(args);

// Redis como almacen distribuido
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));

builder.Services.AddIdempotencyStore<RedisIdempotencyStore>();
builder.Services.AddIdempotencySerializer<NewtonsoftIdempotencySerializer>(); // opcional

builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();
    config.AddIdempotencyBehavior();
    config.AddRequestBehavior<ValidationBehavior<,>>();
});

var app = builder.Build();
app.MapControllers();
app.Run();
```

---

## Siguientes Pasos

- **[Observabilidad](16-observabilidad.md)** — Trazas, metricas y observers para el pipeline
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Componer comportamientos transversales
- **[Result](10-resultado.md)** — Manejo de resultados tipados en handlers
