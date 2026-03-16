# Instalación

## Paquete NuGet

Instala Vali-Mediator desde NuGet:

```sh
dotnet add package Vali-Mediator
```

O mediante la Consola del Administrador de Paquetes:

```powershell
Install-Package Vali-Mediator
```

O editando directamente tu `.csproj`:

```xml
<PackageReference Include="Vali-Mediator" Version="2.0.0" />
```

**Frameworks compatibles:** .NET 7, .NET 8, .NET 9

**Dependencias:** Solo `Microsoft.Extensions.DependencyInjection.Abstractions` — ninguna librería de terceros.

---

## Configuración Básica de DI

Registra Vali-Mediator en `Program.cs` usando `AddValiMediator`:

```csharp
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValiMediator(config =>
{
    // Escanea este assembly en busca de todos los handlers, processors y stream handlers
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
});

var app = builder.Build();
app.Run();
```

`AddValiMediator` registra `IValiMediator` como **Scoped** y auto-descubre todos los handlers en el assembly especificado.

---

## Usando RegisterServicesFromAssemblyContaining

Si los handlers están en un assembly separado (por ejemplo, un proyecto `Application`), usa la sobrecarga genérica:

```csharp
builder.Services.AddValiMediator(config =>
{
    // Registra el assembly que contiene CreateOrderHandler
    config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
});
```

---

## Registrando Múltiples Assemblies

Puedes registrar handlers desde múltiples assemblies:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(
        typeof(OrdersApplicationModule).Assembly);

    config.RegisterServicesFromAssembly(
        typeof(InventoryApplicationModule).Assembly);
});
```

Los assemblies duplicados se ignoran automáticamente.

---

## Agregando Pipeline Behaviors

Los behaviors se registran explícitamente usando `AddBehavior`, `AddRequestBehavior<T>` o `AddDispatchBehavior<T>`:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();

    // Para handlers IRequest<TResponse> (dos parámetros de tipo)
    config.AddRequestBehavior<LoggingBehavior<,>>();
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Para handlers INotification e IFireAndForget (un parámetro de tipo)
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>();
});
```

Los behaviors se aplican en orden de registro: el **primero registrado** es el **más externo** en el pipeline.

---

## Controlando el ServiceLifetime

Por defecto, todos los handlers descubiertos se registran como **Scoped**. Puedes cambiar esto por assembly o por behavior:

```csharp
builder.Services.AddValiMediator(config =>
{
    // Handlers registrados como Transient
    config.RegisterServicesFromAssembly(
        typeof(Program).Assembly,
        ServiceLifetime.Transient);

    // Behavior sin estado registrado como Singleton
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
});
```

---

## Ejemplo Completo de Program.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Servicios de aplicación
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Registro de Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Pipeline: Logging → Validation → Handler → Validation → Logging
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Pipeline de dispatch para notificaciones y fire-and-forget
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>(ServiceLifetime.Singleton);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
```

---

## Inyectando IValiMediator

Inyecta `IValiMediator` donde necesites enviar peticiones:

```csharp
// En un controller
public class OrdersController : ControllerBase
{
    private readonly IValiMediator _mediator;

    public OrdersController(IValiMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateOrderCommand(request), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}

// En una Minimal API
app.MapPost("/orders", async (CreateOrderRequest req, IValiMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new CreateOrderCommand(req), ct);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

---

## Siguientes Pasos

- **[Inicio Rápido](03-inicio-rapido.md)** — Construye tu primera petición/handler completa
- **[Inyección de Dependencias](12-inyeccion-dependencias.md)** — Referencia completa de todas las opciones de registro
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Implementa behaviors de logging, validación y timing
