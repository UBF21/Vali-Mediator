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
    config.AddRequestBehavior(typeof(ValidationBehavior<,>));
});

var app = builder.Build();
app.MapControllers();
app.Run();
```

---

## Alcance, Verificacion de Payload y Limites

- **Alcance.** `IIdempotent.IdempotencyScope` (por defecto `null`) forma parte de la clave almacenada. Devuelve la identidad del llamador (usuario, tenant) en toda peticion cuya `IdempotencyKey` la envia el cliente; de lo contrario dos usuarios con la misma clave recibirian la respuesta del otro.
- **Verificacion de payload.** Se guarda una huella SHA-256 de la peticion serializada. Reusar una clave con otro payload devuelve `Result.Fail(..., ErrorType.Conflict)` si la respuesta es `Result`/`Result<T>`, o lanza `IdempotencyConflictException` en otro caso. Se desactiva con `AddIdempotencyOptions(o => o.VerifyRequestFingerprint = false)` si las peticiones llevan campos volatiles (marcas de tiempo, ids de correlacion). Las peticiones no serializables no se verifican.
- **Los fallos no se guardan.** Un `Result` fallido se devuelve al llamador pero nunca se reproduce, asi un error transitorio no queda pegado a la clave.
- **Los nombres de tipo no dependen de la version.** Las entradas escritas con una version anterior del ensamblado siguen reproduciendose tras actualizar.

| Opcion | Por defecto | Efecto |
|--------|-------------|--------|
| `IdempotencyOptions.MaxKeyLength` | 256 | Largo maximo de `IdempotencyKey` e `IdempotencyScope`; claves mas largas o vacias lanzan `ArgumentException` |
| `IdempotencyOptions.VerifyRequestFingerprint` | `true` | Detecta el reuso de la clave con otro payload |
| `InMemoryIdempotencyStoreOptions.MaxEntries` | 10.000 | Se expulsan primero las entradas mas antiguas |
| `InMemoryIdempotencyStoreOptions.DefaultExpiration` | 24 horas | Se aplica cuando `Expiration` es `null`; `null` lo desactiva |

```csharp
services.AddIdempotencyOptions(o => o.MaxKeyLength = 128);
services.AddInMemoryIdempotencyStore(o => { o.MaxEntries = 50_000; o.DefaultExpiration = TimeSpan.FromHours(6); });
```

### Varias instancias compartiendo un store (reserva atomica)

El bloqueo por clave vive dentro de un proceso. Cuando varias instancias comparten un store (Redis, SQL, ...) y reciben la misma clave a la vez, cada una ejecutaria el handler. Para evitarlo, un store puede adherirse a una **reserva atomica** mediante tres miembros por defecto de `IIdempotencyStore` (los stores existentes siguen funcionando igual; simplemente no se adhieren):

| Miembro | Contrato |
|---------|----------|
| `bool SupportsReservation` (por defecto `false`) | Devuelve `true` para activar el flujo de reserva |
| `Task<string?> TryReserveAsync(key, lease, ct)` | Reclama `key` de forma atomica durante `lease`; devuelve un token opaco, o `null` si otro la tiene (Redis: `SET key token NX PX lease`) |
| `Task ReleaseReservationAsync(key, token, ct)` | Libera solo si `token` sigue siendo el dueno (Redis: compare-and-delete con un script Lua) |

Con `SupportsReservation`, el behavior ejecuta: reproducir si hay respuesta → reservar → ejecutar el handler → guardar la respuesta → liberar (siempre, tambien ante fallo o cancelacion). Quien no gana la reserva consulta hasta que aparece la respuesta del ganador (se reproduce) o la reserva se libera (lo intenta de nuevo). `InMemoryIdempotencyStore` implementa el contrato.

| Opcion (`IdempotencyOptions`) | Por defecto | Efecto |
|--------|-------------|--------|
| `ReservationLease` | 30 s | Vida de una reserva que nunca se libera (instancia caida). **Debe superar la duracion maxima del handler**, o otra instancia puede iniciar el mismo trabajo |
| `ReservationWaitTimeout` | 30 s | Cuanto espera un llamador a otra instancia antes de recibir `Result.Fail(..., ErrorType.Conflict)` (o `IdempotencyInProgressException` en respuestas que no son `Result`); el cliente puede reintentar |
| `ReservationPollInterval` | 25 ms | Cada cuanto consulta un llamador en espera si ya hay respuesta |

Un store inalcanzable debe fallar cerrado (dejar propagar la excepcion): suponer "no visto antes" ejecutaria un pago dos veces.

---

## Siguientes Pasos

- **[Observabilidad](16-observabilidad.md)** — Trazas, metricas y observers para el pipeline
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Componer comportamientos transversales
- **[Result](10-resultado.md)** — Manejo de resultados tipados en handlers
