#  <img src="https://github.com/UBF21/Vali-Mediator/blob/main/Vali-Mediator/Vali_Mediator_logo.png?raw=true" alt="Logo de Vali Mediator" style="width: 46px; height: 46px; max-width: 300px;">   Vali-Mediator - Lightweight Mediator Pattern for .NET  

## Introduction 🚀
Welcome to Vali-Mediator, a lightweight .NET library that implements the Mediator pattern with CQRS support. Designed to simplify request handling, pipeline behaviors, and event notifications, Vali-Mediator offers a fluent and extensible API. It integrates seamlessly with dependency injection, making it perfect for managing commands, queries, notifications, fire-and-forget operations, and compensation flows in your .NET applications.

## Installation 📦
To add Vali-Mediator to your .NET project, install it via NuGet with the following command:

```sh
dotnet add package Vali-Mediator
```

Ensure your project targets a compatible .NET version (e.g., .NET 7.0, 8.0, or 9.0). Vali-Mediator is lightweight and depends only on **Microsoft.Extensions.DependencyInjection.Abstractions**, ensuring easy integration into any .NET application.

## Usage 🛠️

Vali-Mediator enables you to send requests, publish notifications, and dispatch fire-and-forget commands through the **IValiMediator** interface. It supports pipeline behaviors (e.g., validation, logging) for all dispatchable types (**IDispatch**), including requests, notifications, and fire-and-forget commands. Additionally, it now supports prioritized notification handling and pre/post-processing for enhanced control over request and dispatch lifecycles.

### Configuration Example

Configure Vali-Mediator in your application’s startup (e.g., **Program.cs** in ASP.NET Core):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Vali_Mediator.Core.General.Extension;

var builder = WebApplication.CreateBuilder(args);

// Add Vali-Mediator services
builder.Services.AddValiMediator(config =>
{
    // Register assembly containing handlers and notifications
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    // Add a pipeline behavior for all dispatchable types (e.g., validation)
    config.AddBehavior(typeof(IPipelineBehavior<>), typeof(ValidationBehavior<>));
    // Add a pipeline behavior for all IRequest types (e.g., validation)
    config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    // Add pre-processor and post-processor for a specific request
    config.AddRequestPreProcessor(typeof(IRequestPreProcessor<CreateProductCommand, int>), typeof(ProductAuditPreProcessor));
    config.AddRequestPostProcessor(typeof(IRequestPostProcessor<CreateProductCommand, int>), typeof(ProductAuditPostProcessor));
});

var app = builder.Build();
app.Run();
```

### Basic Example: Sending a Request

Define a request and its handler, then send it using the mediator:

```csharp
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.General.Mediator;

// Define a request
public class CreateProductCommand : IRequest<int>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// Define the handler
public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, int>
{
    public Task<int> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        // Simulate saving to a database and returning an ID
        return Task.FromResult(42);
    }
}

// Usage in a controller or service
public class ProductService
{
    private readonly IValiMediator _mediator;

    public ProductService(IValiMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<int> CreateProduct(string name, decimal price)
    {
        var command = new CreateProductCommand { Name = name, Price = price };
        return await _mediator.Send(command);
    }
}
```

### Basic Example: Publish a Notification

Define a notification and its handler with priority, then publish it:

```csharp
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.General.Mediator;

// Define a notification
public class ProductCreatedNotification : INotification
{
    public int ProductId { get; set; }
}

// Define a handler with priority
public class ProductCreatedLogger : INotificationHandler<ProductCreatedNotification>
{
    public Task Handle(ProductCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Product {notification.ProductId} created.");
        return Task.CompletedTask;
    }

    public int Priority => 1; // Lower priority
}

// Define another handler with higher priority
public class ProductCreatedEmailSender : INotificationHandler<ProductCreatedNotification>
{
    public Task Handle(ProductCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Sending email for product {notification.ProductId}.");
        return Task.CompletedTask;
    }

    public int Priority => 2; // Higher priority, executes first
}

// Usage
public class ProductService
{
    private readonly IValiMediator _mediator;

    public ProductService(IValiMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task NotifyProductCreated(int productId)
    {
        var notification = new ProductCreatedNotification { ProductId = productId };
        await _mediator.Publish(notification);
    }
}
```

### New Feature: Fire-and-Forget Commands

Vali-Mediator supports `IFireAndForget` commands, allowing you to dispatch asynchronous operations without waiting for a response. This is ideal for scenarios like logging or sending emails.

#### Example: Sending a Fire-and-Forget Command

```csharp
using Vali_Mediator.Core.FireAndForget;
using Vali_Mediator.Core.General.Mediator;

// Define a fire-and-forget command
public class LogEventCommand : IFireAndForget
{
    public string EventMessage { get; set; }
}

// Define the handler
public class LogEventHandler : IFireAndForgetHandler<LogEventCommand>
{
    public Task Handle(LogEventCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Logging event: {command.EventMessage}");
        return Task.CompletedTask;
    }
}

// Usage
public class EventService
{
    private readonly IValiMediator _mediator;

    public EventService(IValiMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task LogEventAsync(string message)
    {
        var command = new LogEventCommand { EventMessage = message };
        await _mediator.Send(command);
    }
}
```

### Compensation Flows (Saga Pattern)

Vali-Mediator includes support for compensation flows based on the Saga pattern, allowing you to define remediation actions if a normal flow fails.

#### Example: Implementing Compensation

```csharp
using Vali_Mediator.Core.Compensable;
using Vali_Mediator.Core.Request;
using Vali_Mediator.Core.General.Mediator;

// Define a compensable request
public class CreateOrderCommand : Compensable, IRequest<bool>
{
    public string OrderId { get; set; }

    public override IFireAndForget GetCompensation()
    {
        return new CancelOrderCommand { OrderId = OrderId };
    }
}

// Define the handler with compensation
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, bool>
{
    private readonly IValiMediator _mediator;

    public CreateOrderHandler(IValiMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<bool> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Simulate order creation
            Console.WriteLine($"Order {request.OrderId} created.");
            return true;
        }
        catch (Exception)
        {
            // If something fails, compensate
            await request.Compensate(_mediator, cancellationToken);
            throw;
        }
    }
}

// Define the compensation command
public class CancelOrderCommand : IFireAndForget
{
    public string OrderId { get; set; }
}

// Define the compensation handler
public class CancelOrderHandler : IFireAndForgetHandler<CancelOrderCommand>
{
    public Task Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Order {command.OrderId} cancelled due to failure.");
        return Task.CompletedTask;
    }
}
```

### New Feature: Notification Handler Priority

Vali-Mediator now supports prioritized notification handling through the `Priority` property in `INotificationHandler<TNotification>`. Handlers with higher priority values are executed first, allowing you to control the order of operations, such as sending an email before updating inventory.

#### Example: Prioritized Notification Handlers

```csharp
using Vali_Mediator.Core.Notification;

// Define a notification
public class OrderCreatedNotification : INotification
{
    public int OrderId { get; set; }
    public string ProductName { get; set; }
}

// High-priority handler
public class EmailNotificationHandler : INotificationHandler<OrderCreatedNotification>
{
    public Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Priority 2] Sending email for order {notification.OrderId}: {notification.ProductName}");
        return Task.CompletedTask;
    }

    public int Priority => 2; // Executes first
}

// Low-priority handler
public class InventoryNotificationHandler : INotificationHandler<OrderCreatedNotification>
{
    public Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Priority 1] Updating inventory for order {notification.OrderId}: {notification.ProductName}");
        return Task.CompletedTask;
    }

    public int Priority => 1; // Executes second
}

// Usage
public async Task NotifyOrderCreated(IValiMediator mediator, int orderId, string productName)
{
    var notification = new OrderCreatedNotification { OrderId = orderId, ProductName = productName };
    await mediator.Publish(notification);
}
```

**Output**:
```
[Priority 2] Sending email for order 1001: Smartphone
[Priority 1] Updating inventory for order 1001: Smartphone
```

### New Feature: Pre-Processors and Post-Processors

Vali-Mediator now supports pre-processors and post-processors for requests (`IRequest<TResponse>`) and dispatchable types (`IDispatch`, including `INotification` and `IFireAndForget`). Pre-processors run before the handler to perform validations or logging, while post-processors run after to process results or trigger secondary actions.

#### Example: Pre-Processor and Post-Processor for a Request

```csharp
using Vali_Mediator.Core.General.Processing;
using Vali_Mediator.Core.Request;

// Define a request
public class CreateProductCommand : IRequest<int>
{
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// Define pre-processor
public class ProductAuditPreProcessor : IRequestPreProcessor<CreateProductCommand, int>
{
    public void Process(CreateProductCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Product name cannot be empty.");
        }
        Console.WriteLine($"[Pre-Processor] Validating CreateProductCommand: Name={request.Name}, Price={request.Price}");
    }
}

// Define post-processor
public class ProductAuditPostProcessor : IRequestPostProcessor<CreateProductCommand, int>
{
    public void Process(CreateProductCommand request, int response, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Post-Processor] CreateProductCommand completed: ProductId={response}, Name={request.Name}");
    }
}

// Register in configuration
services.AddValiMediator(config =>
{
    config
        .RegisterServicesFromAssembly(typeof(Program).Assembly)
        .AddRequestPreProcessor(typeof(IRequestPreProcessor<CreateProductCommand, int>), typeof(ProductAuditPreProcessor))
        .AddRequestPostProcessor(typeof(IRequestPostProcessor<CreateProductCommand, int>), typeof(ProductAuditPostProcessor));
});

// Usage
public async Task CreateProduct(IValiMediator mediator, string name, decimal price)
{
    var command = new CreateProductCommand { Name = name, Price = price };
    int productId = await mediator.Send(command);
    Console.WriteLine($"Product created with ID: {productId}");
}
```

**Output**:
```
[Pre-Processor] Validating CreateProductCommand: Name=Laptop, Price=999.99
[Post-Processor] CreateProductCommand completed: ProductId=42, Name=Laptop
Product created with ID: 42
```

#### Example: Pre-Processor and Post-Processor for a Fire-and-Forget Command

```csharp
using Vali_Mediator.Core.General.Processing;
using Vali_Mediator.Core.FireAndForget;

// Define a fire-and-forget command
public class LogEventCommand : IFireAndForget
{
    public string EventMessage { get; set; }
}

// Define pre-processor
public class LogAuditPreProcessor : IPreProcessor<LogEventCommand>
{
    public void Process(LogEventCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.EventMessage))
        {
            throw new ArgumentException("Event message cannot be empty.");
        }
        Console.WriteLine($"[Pre-Processor] Validating LogEventCommand: Message={command.EventMessage}");
    }
}

// Define post-processor
public class LogAuditPostProcessor : IPostProcessor<LogEventCommand>
{
    public void Process(LogEventCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Post-Processor] LogEventCommand completed: Message={command.EventMessage}");
    }
}

// Register in configuration
services.AddValiMediator(config =>
{
    config
        .RegisterServicesFromAssembly(typeof(Program).Assembly)
        .AddPreProcessor(typeof(IPreProcessor<LogEventCommand>), typeof(LogAuditPreProcessor))
        .AddPostProcessor(typeof(IPostProcessor<LogEventCommand>), typeof(LogAuditPostProcessor));
});

// Usage
public async Task LogEvent(IValiMediator mediator, string message)
{
    var command = new LogEventCommand { EventMessage = message };
    await mediator.Send(command);
}
```

**Output**:
```
[Pre-Processor] Validating LogEventCommand: Message=User logged in
Logging event: User logged in
[Post-Processor] LogEventCommand completed: Message=User logged in
```

### Key Methods 📝

Vali-Mediator provides two primary methods through **IValiMediator**:

#### Send (for Requests and Fire-and-Forget) 🏗️

- For requests with a response:

```csharp
var command = new CreateProductCommand { Name = "Laptop", Price = 999.99m };
int productId = await mediator.Send(command);
```

- For fire-and-forget commands:

```csharp
var logCommand = new LogEventCommand { EventMessage = "User logged in" };
await mediator.Send(logCommand);
```

#### Publish (for Notifications) 🔄

Publishes a notification to all registered handlers, ordered by priority:

```csharp
var notification = new ProductCreatedNotification { ProductId = 42 };
await mediator.Publish(notification);
```

### Building Complex Pipelines 🧩

Vali-Mediator supports pipeline behaviors for all dispatchable types (**IDispatch**), including requests, notifications, and fire-and-forget commands. This allows you to apply cross-cutting concerns like validation, logging, or authorization.

#### Example: Validation Behavior for All Dispatchable Types

```csharp
using Vali_Mediator.Core.General.Behavior;

public class ValidationBehavior<TDispatch> : IPipelineBehavior<TDispatch> where TDispatch : IDispatch
{
    public async Task Handle(TDispatch dispatch, Func<Task> next, CancellationToken cancellationToken)
    {
        Console.WriteLine("Validating dispatch...");
        await next();
    }
}

// Register in configuration
services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    config.AddBehavior(typeof(IPipelineBehavior<>), typeof(ValidationBehavior<>));
});
```

#### Example: Validation Behavior for IRequest

```csharp
using Vali_Mediator.Core.General.Behavior;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        Console.WriteLine("Validating request...");
        return await next();
    }
}

// Register in configuration
services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});
```

If you need to use implementations like `INotification`, `IFireAndForget`, and `IRequest`, you need to create the two behaviors:

- `public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>` for `IRequest`
- `public interface IPipelineBehavior<in TRequest> where TRequest : IDispatch` for `INotification` and `IFireAndForget`

These are separated to adhere to the single responsibility principle, as their behaviors are similar but distinct.

### Comparison: Without vs. With Vali-Mediator ⚖️

#### Without Vali-Mediator (Manual Handling)

Manually resolving and calling handlers is verbose and tightly coupled:

```csharp
var handler = serviceProvider.GetService<CreateProductCommandHandler>();
int productId = await handler.Handle(new CreateProductCommand { Name = "Laptop", Price = 999.99m }, CancellationToken.None);
```

#### With Vali-Mediator

Vali-Mediator decouples the process with a clean, unified API:

```csharp
int productId = await mediator.Send(new CreateProductCommand { Name = "Laptop", Price = 999.99m });
```

## Features and Enhancements 🌟

### Recent Updates

- **Notification Handler Priority**: Added the `Priority` property to `INotificationHandler<TNotification>`, allowing handlers to be executed in a user-defined order (higher priority values execute first). This is useful for scenarios where certain actions (e.g., sending emails) must precede others (e.g., updating inventory).
- **Pre-Processors and Post-Processors**: Introduced support for pre-processors and post-processors for `IRequest<TResponse>`, `INotification`, and `IFireAndForget`. Pre-processors run before handlers to perform validations or logging, while post-processors run after to process results or trigger secondary actions.
- **Fire-and-Forget Support**: Added `IFireAndForget` for asynchronous operations without blocking for a response.
- **Compensation Flows (Saga Pattern)**: Introduced compensation mechanisms to handle remediation for failed operations, ensuring data consistency.
- **Extended Pipeline Behaviors**: Pipeline behaviors now apply to all dispatchable types (`IDispatch`), including requests, notifications, and fire-and-forget commands.
- **Multi-targeting support** for .NET 7, 8, and 9.

### Planned Features

- Enhanced debugging tools for pipeline execution.
- Support for advanced compensation flow orchestration.

Follow the project on GitHub for updates on new features and improvements!

## Donations 💖
If you find **Vali-Mediator** useful and would like to support its development, consider making a donation:

- **For Latin America**: [Donate via MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **For International Donations**: [Donate via PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

Your contributions help keep this project alive and improve its development! 🚀

## License 📜
This project is licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

## Contributions 🤝
Feel free to open issues and submit pull requests to improve this library!
