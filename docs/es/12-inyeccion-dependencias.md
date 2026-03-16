# Inyección de Dependencias

Este artículo cubre la referencia completa para registrar los servicios de Vali-Mediator con el contenedor de DI de .NET.

---

## AddValiMediator

El punto de entrada para el registro. Llámalo una vez en `Program.cs` (o `Startup.cs`):

```csharp
using Vali_Mediator.Core.General.Extension;

builder.Services.AddValiMediator(config =>
{
    // La configuración va aquí
});
```

`AddValiMediator`:
1. Registra `IValiMediator` como **Scoped** (una instancia por scope de petición)
2. Escanea los assemblies especificados en busca de handlers, processors y stream handlers
3. Registra todos los behaviors y processors especificados explícitamente

---

## Registro de Assemblies

### RegisterServicesFromAssembly

Escanea un assembly y registra todos los handlers descubiertos:

```csharp
config.RegisterServicesFromAssembly(typeof(Program).Assembly);

// Con un lifetime personalizado (por defecto es Scoped)
config.RegisterServicesFromAssembly(
    typeof(Program).Assembly,
    ServiceLifetime.Transient);
```

Tipos descubiertos:
- `IRequestHandler<TRequest, TResponse>`
- `INotificationHandler<TNotification>`
- `IFireAndForgetHandler<TFireAndForget>`
- `IStreamRequestHandler<TRequest, TResponse>`
- `IPreProcessor<TDispatch>` e `IPreProcessor<TRequest, TResponse>`
- `IPostProcessor<TDispatch>` e `IPostProcessor<TRequest, TResponse>`

### RegisterServicesFromAssemblyContaining\<T\>

Sobrecarga de conveniencia que resuelve el assembly desde un tipo:

```csharp
// Registra el assembly que contiene CreateOrderHandler
config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();

// Útil cuando los handlers están en un proyecto Application separado
config.RegisterServicesFromAssemblyContaining<ApplicationModule>();

// Con un lifetime personalizado
config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>(ServiceLifetime.Singleton);
```

### Múltiples Assemblies

```csharp
config.RegisterServicesFromAssemblyContaining<OrdersApplicationModule>();
config.RegisterServicesFromAssemblyContaining<InventoryApplicationModule>();
config.RegisterServicesFromAssemblyContaining<NotificationsApplicationModule>();
```

Los assemblies duplicados se detectan e ignoran automáticamente.

---

## Registro de Behaviors

### AddRequestBehavior\<TImplementation\>

Registra un behavior open-generic para handlers `IRequest<TResponse>`:

```csharp
// TImplementation debe ser un tipo open-generic
config.AddRequestBehavior<LoggingBehavior<,>>();
config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
config.AddRequestBehavior<ValidationBehavior<,>>();
```

Equivalente a:

```csharp
config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### AddDispatchBehavior\<TImplementation\>

Registra un behavior open-generic para handlers `INotification` e `IFireAndForget`:

```csharp
config.AddDispatchBehavior<NotificationLoggingBehavior<>>();
config.AddDispatchBehavior<AuditBehavior<>>(ServiceLifetime.Singleton);
```

Equivalente a:

```csharp
config.AddBehavior(typeof(IPipelineBehavior<>), typeof(NotificationLoggingBehavior<>));
```

### AddBehavior (nivel bajo)

La API explícita a la que delegan `AddRequestBehavior` y `AddDispatchBehavior`:

```csharp
// Para behaviors de IRequest<TResponse>
config.AddBehavior(
    typeof(IPipelineBehavior<,>),
    typeof(LoggingBehavior<,>),
    ServiceLifetime.Singleton);

// Para behaviors de INotification / IFireAndForget
config.AddBehavior(
    typeof(IPipelineBehavior<>),
    typeof(NotificationLoggingBehavior<>));
```

`AddBehavior` valida que el tipo de interfaz sea una interfaz válida de pipeline behavior y lanza `ArgumentException` si no lo es.

---

## Registro de Processors

### Auto-Descubrimiento (recomendado)

Los processors se descubren automáticamente cuando `RegisterServicesFromAssembly` escanea el assembly. No se requiere registro explícito:

```csharp
config.RegisterServicesFromAssemblyContaining<Program>();
// Todas las implementaciones de IPreProcessor<> e IPostProcessor<> se descubren automáticamente
```

### Registro Explícito

Usa el registro explícito cuando los processors están en un assembly diferente o requieren un lifetime específico:

```csharp
// IPreProcessor<TDispatch> — para INotification o IFireAndForget
config.AddPreProcessor(
    typeof(IPreProcessor<OrderPlacedNotification>),
    typeof(OrderPlacedAuditPreProcessor));

// IPostProcessor<TDispatch>
config.AddPostProcessor(
    typeof(IPostProcessor<OrderPlacedNotification>),
    typeof(OrderMetricsPostProcessor),
    ServiceLifetime.Singleton);

// IPreProcessor<TRequest, TResponse> — para IRequest<TResponse>
config.AddRequestPreProcessor(
    typeof(IPreProcessor<CreateOrderCommand, Result<Guid>>),
    typeof(CreateOrderValidationPreProcessor));

// IPostProcessor<TRequest, TResponse>
config.AddRequestPostProcessor(
    typeof(IPostProcessor<CreateOrderCommand, Result<Guid>>),
    typeof(CreateOrderCachePostProcessor),
    ServiceLifetime.Scoped);
```

---

## ServiceLifetime

Todos los métodos de registro aceptan un parámetro `ServiceLifetime` opcional:

| Lifetime | Descripción | Recomendado para |
|---|---|---|
| `Scoped` (por defecto) | Una instancia por scope de petición | Handlers que usan repositorios o DbContext |
| `Transient` | Nueva instancia por cada uso | Handlers ligeros y sin estado |
| `Singleton` | Una instancia para toda la vida de la aplicación | Behaviors sin estado (logging, timing, caché) |

> `IValiMediator` siempre se registra como **Scoped**, independientemente del lifetime del handler.

```csharp
builder.Services.AddValiMediator(config =>
{
    // Handlers: Scoped (por defecto) — acceden a DbContext por petición
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Behaviors sin estado: Singleton — evita alocaciones en cada petición
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);

    // Behavior con estado: Scoped — usa servicios por petición
    config.AddRequestBehavior<ValidationBehavior<,>>(ServiceLifetime.Scoped);

    // Dispatch behaviors
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>(ServiceLifetime.Singleton);
});
```

---

## Ejemplo Completo de Program.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

// Infraestructura
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    // Assemblies de handlers
    config.RegisterServicesFromAssemblyContaining<CreateOrderCommandHandler>();
    config.RegisterServicesFromAssemblyContaining<InventoryHandler>();

    // Pipeline de peticiones: Logging → Timing → Validation → Handler
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Pipeline de dispatch: Logging → Handler
    config.AddDispatchBehavior<DispatchLoggingBehavior<>>(ServiceLifetime.Singleton);
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Resumen de la API de Configuración Fluent

| Método | Qué registra | Notas |
|---|---|---|
| `RegisterServicesFromAssembly(assembly, lifetime)` | Todos los handlers y processors auto-descubiertos del assembly | Se ignoran duplicados |
| `RegisterServicesFromAssemblyContaining<T>(lifetime)` | Igual que lo anterior, resuelve el assembly desde `typeof(T)` | Sobrecarga de conveniencia |
| `AddRequestBehavior<TImpl>(lifetime)` | `IPipelineBehavior<,>` | Para el pipeline de `IRequest<TResponse>` |
| `AddDispatchBehavior<TImpl>(lifetime)` | `IPipelineBehavior<>` | Para el pipeline de `INotification` e `IFireAndForget` |
| `AddBehavior(interface, impl, lifetime)` | Cualquiera de las interfaces de behavior | Nivel bajo; valida el tipo de interfaz |
| `AddPreProcessor(interface, impl, lifetime)` | `IPreProcessor<TDispatch>` | Pre-processor explícito para tipos dispatch |
| `AddPostProcessor(interface, impl, lifetime)` | `IPostProcessor<TDispatch>` | Post-processor explícito para tipos dispatch |
| `AddRequestPreProcessor(interface, impl, lifetime)` | `IPreProcessor<TRequest, TResponse>` | Pre-processor explícito para tipos request |
| `AddRequestPostProcessor(interface, impl, lifetime)` | `IPostProcessor<TRequest, TResponse>` | Post-processor explícito para tipos request |

---

## Siguientes Pasos

- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Implementar `IPipelineBehavior`
- **[Procesadores](09-procesadores.md)** — Implementar `IPreProcessor` / `IPostProcessor`
- **[Integración ASP.NET Core](13-integracion-aspnetcore.md)** — Mapear resultados a respuestas HTTP
