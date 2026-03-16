# Installation

## NuGet Package

Install Vali-Mediator from NuGet:

```sh
dotnet add package Vali-Mediator
```

Or via the Package Manager Console:

```powershell
Install-Package Vali-Mediator
```

Or by editing your `.csproj` directly:

```xml
<PackageReference Include="Vali-Mediator" Version="2.0.0" />
```

**Supported frameworks:** .NET 7, .NET 8, .NET 9

**Dependencies:** Only `Microsoft.Extensions.DependencyInjection.Abstractions` — no third-party libraries.

---

## Basic DI Setup

Register Vali-Mediator in `Program.cs` using `AddValiMediator`:

```csharp
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValiMediator(config =>
{
    // Scan this assembly for all handlers, processors, and stream handlers
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
});

var app = builder.Build();
app.Run();
```

`AddValiMediator` registers `IValiMediator` as **Scoped** and auto-discovers all handlers in the specified assembly.

---

## Using RegisterServicesFromAssemblyContaining

If your handlers live in a separate assembly (e.g., an `Application` project), use the generic overload:

```csharp
builder.Services.AddValiMediator(config =>
{
    // Registers the assembly that contains CreateOrderHandler
    config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();
});
```

---

## Registering Multiple Assemblies

You can register handlers from multiple assemblies:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(
        typeof(OrdersApplicationModule).Assembly);

    config.RegisterServicesFromAssembly(
        typeof(InventoryApplicationModule).Assembly);
});
```

Duplicate assemblies are automatically ignored.

---

## Adding Pipeline Behaviors

Behaviors are registered explicitly using `AddBehavior`, `AddRequestBehavior<T>`, or `AddDispatchBehavior<T>`:

```csharp
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<CreateOrderHandler>();

    // For IRequest<TResponse> handlers (two type parameters)
    config.AddRequestBehavior<LoggingBehavior<,>>();
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // For INotification and IFireAndForget handlers (one type parameter)
    config.AddDispatchBehavior<NotificationLoggingBehavior<>>();
});
```

Behaviors are applied in registration order: the **first registered** is the **outermost** in the pipeline.

---

## Controlling Service Lifetime

By default, all discovered handlers are registered as **Scoped**. You can change this per assembly or per behavior:

```csharp
builder.Services.AddValiMediator(config =>
{
    // Handlers registered as Transient
    config.RegisterServicesFromAssembly(
        typeof(Program).Assembly,
        ServiceLifetime.Transient);

    // Stateless behavior registered as Singleton
    config.AddRequestBehavior<TimingBehavior<,>>(ServiceLifetime.Singleton);
});
```

---

## Complete Program.cs Example

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register application services
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Register Vali-Mediator
builder.Services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssemblyContaining<Program>();

    // Pipeline: Logging → Validation → Handler → Validation → Logging
    config.AddRequestBehavior<LoggingBehavior<,>>(ServiceLifetime.Singleton);
    config.AddRequestBehavior<ValidationBehavior<,>>();

    // Dispatch pipeline for notifications and fire-and-forget
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

## Injecting IValiMediator

Inject `IValiMediator` wherever you need to send requests:

```csharp
// In a controller
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

// In a Minimal API
app.MapPost("/orders", async (CreateOrderRequest req, IValiMediator mediator, CancellationToken ct) =>
{
    var result = await mediator.Send(new CreateOrderCommand(req), ct);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

---

## Next Steps

- **[Quick Start](03-quick-start.md)** — Build your first complete request/handler
- **[Dependency Injection](12-dependency-injection.md)** — Full reference for all registration options
- **[Pipeline Behaviors](08-pipeline-behaviors.md)** — Implement logging, validation, and timing behaviors
