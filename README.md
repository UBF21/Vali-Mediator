#  <img src="https://github.com/UBF21/Vali-Mediator/blob/main/Vali-Mediator/logo.png?raw=true" alt="Logo de Vali Mediator" style="width: 46px; height: 46px; max-width: 300px;">   Vali-Mediator - Lightweight Mediator Pattern for .NET  


## Introduction 🚀
Welcome to Vali-Mediator, a lightweight .NET library that implements the Mediator pattern with CQRS support. Designed to simplify request handling, pipeline behaviors, and event notifications, Vali-Mediator offers a fluent and extensible API. It integrates seamlessly with dependency injection, making it perfect for managing commands, queries, notifications, and now, fire-and-forget operations and compensation flows in your .NET applications.

## Installation 📦
To add Vali-Mediator to your .NET project, install it via NuGet with the following command:

```sh
dotnet add package Vali-Mediator
```
Ensure your project targets a compatible .NET version (e.g., .NET 7.0, 8.0, or 9.0). Vali-Mediator is lightweight and depends only on **Microsoft.Extensions.DependencyInjection.Abstractions**, ensuring easy integration into any .NET application.

## Usage 🛠️

Vali-Mediator enables you to send requests, publish notifications, and now, dispatch fire-and-forget commands through the **IValiMediator** interface. It supports pipeline behaviors (e.g., validation, logging) for all dispatchable types (**IDispatch**), including requests, notifications, and fire-and-forget commands.

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
 // Add a pipeline behavior for all IRequest's types (e.g., validation)
    config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
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
### Basic Example: Sending a Request

Define a notification and its handler, then publish it:

```csharp
using Vali_Mediator.Core.Notification;
using Vali_Mediator.Core.General.Mediator;

// Define a notification
public class ProductCreatedNotification : INotification
{
    public int ProductId { get; set; }
}

// Define a handler
public class ProductCreatedLogger : INotificationHandler<ProductCreatedNotification>
{
    public Task Handle(ProductCreatedNotification notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Product {notification.ProductId} created.");
        return Task.CompletedTask;
    }
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
### New Feature: **Fire-and-Forget** Commands

Vali-Mediator now supports IFireAndForget commands, allowing you to dispatch asynchronous operations without waiting for a response. This is ideal for scenarios like logging, sending emails, or any operation where you don’t need to block for a result.

### Example: Sending a Fire-and-Forget Command

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
### New Feature: Compensation Flows (Saga Pattern)

Vali-Mediator now includes support for compensation flows based on the Saga pattern, allowing you to define remediation actions if a normal flow fails. This ensures data consistency in distributed systems by automatically executing compensatory actions in case of errors.

### Example: Implementing Compensation

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

Publishes a notification to all registered handlers:

```csharp
var notification = new ProductCreatedNotification { ProductId = 42 };
await mediator.Publish(notification);
```

### Building Complex Pipelines 🧩

Vali-Mediator now supports pipeline behaviors for all dispatchable types (**IDispatch**), including requests, notifications, and fire-and-forget commands. This allows you to apply cross-cutting concerns like validation, logging, or authorization across different types of operations.

#### Example: Validation Behavior for All Dispatchable Types

```csharp
using Vali_Mediator.Core.General.Behavior;

public class ValidationBehavior<TDispatch> : IPipelineBehavior<TDispatch> where TDispatch : IDispatch
{
    public async Task Handle(TDispatch dispatch, Func<Task> next, CancellationToken cancellationToken)
    {
        // Add validation logic here (e.g., throw if invalid)
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
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)    {
        // Add validation logic here (e.g., throw if invalid)
        Console.WriteLine("Validating dispatch...");
        await next();
    }
}

// Register in configuration
services.AddValiMediator(config =>
{
    config.RegisterServicesFromAssembly(typeof(Program).Assembly);
    config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
});
```
If you need to use the implementations like INotification,IFireAndForget and IRequest you need to create the two Behavior.

- **public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>** for ***IRequest***

- **public interface IPipelineBehavior<in TRequest> where TRequest : IDispatch** for ***INotification*** and ***IFireAndForget***

It was decided to create them separately as a matter of principle of responsibility and because the behavior is similar but nevertheless different from each other.

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

- Fire-and-Forget Support: Added IFireAndForget for asynchronous operations without blocking for a response.
- Compensation Flows (Saga Pattern): Introduced compensation mechanisms to handle remediation for failed operations, ensuring data consistency.
- Extended Pipeline Behaviors: Pipeline behaviors now apply to all dispatchable types (IDispatch), including requests, notifications, and fire-and-forget commands.
- Multi-targeting support for .NET 7, 8, and 9.

### Planned Features

- Support for request pre-processing and post-processing hooks.
- Enhanced debugging tools for pipeline execution.

Follow the project on GitHub for updates on new features and improvements!

## Donations 💖
If you find ValiFlow useful and would like to support its development, consider making a donation:

- **For Latin America**: [Donate via MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **For International Donations**: [Donate via PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)


Your contributions help keep this project alive and improve its development! 🚀

## License 📜
This project is licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

## Contributions 🤝
Feel free to open issues and submit pull requests to improve this library!
