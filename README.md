#  <img src="https://github.com/UBF21/Vali-Mediator/blob/main/Vali-Mediator/logo.png?raw=true" alt="Logo de Vali Mediator" style="width: 46px; height: 46px; max-width: 300px;">   Vali-Mediator - Lightweight Mediator Pattern for .NET  


## Introduction 🚀
Welcome to Vali-Mediator, a lightweight .NET library that implements the Mediator pattern with CQRS support. Designed to simplify request handling, pipeline behaviors, and event notifications, Vali-Mediator offers a fluent and extensible API. It integrates seamlessly with dependency injection, making it perfect for managing commands, queries, and notifications in your .NET applications.

## Installation 📦
To add Vali-Mediator to your .NET project, install it via NuGet with the following command:

```sh
dotnet add package Vali-Mediator
```
Ensure your project targets a compatible .NET version (e.g., .NET 7.0, 8.0, or 9.0). Vali-Mediator is lightweight and depends only on Microsoft.Extensions.DependencyInjection.Abstractions, ensuring easy integration into any .NET application.

## Usage 🛠️

Vali-Mediator enables you to send requests to handlers and publish notifications to multiple subscribers through the **IValiMediator** interface. It supports pipeline behaviors (e.g., validation, logging) and leverages dependency injection for service resolution.

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
    // Add a pipeline behavior (e.g., validation)
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
### Basic Example: Publishing a Notification

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

### Key Methods 📝

Vali-Mediator provides two primary methods through **IValiMediator**:

#### Send 🏗️

Sends a request to its registered handler, optionally processing it through a pipeline of behaviors:

```csharp
var command = new CreateProductCommand { Name = "Laptop", Price = 999.99m };
int productId = await mediator.Send(command);
```

#### Publish 🔄

Publishes a notification to all registered handlers for asynchronous processing:

```csharp
var notification = new ProductCreatedNotification { ProductId = 42 };
await mediator.Publish(notification);
```

### Building Complex Pipelines 🧩

**Vali-Mediator** supports pipeline behaviors to add cross-cutting concerns like validation or logging. Here’s an example with a validation behavior:

#### Example: Validation Behavior

```csharp
using Vali_Mediator.Core.General.Behavior;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        // Add validation logic here (e.g., throw if invalid)
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

### Comparison: Without vs. With Vali-Mediator ⚖️

#### Without Vali-Mediator (Manual Handling)

Manually resolving and calling handlers is verbose and tightly coupled:

```csharp
var handler = serviceProvider.GetService<CreateProductCommandHandler>();
int productId = await handler.Handle(new CreateProductCommand { Name = "Laptop", Price = 999.99m }, CancellationToken.None);
```

#### Without Vali-Mediator (Manual Handling)

Vali-Mediator decouples the process with a clean, unified API:

```csharp
int productId = await mediator.Send(new CreateProductCommand { Name = "Laptop", Price = 999.99m });
```


## Features and Enhancements 🌟

### Recent Updates

- Multi-targeting support for .NET 7, 8, and 9.
- Robust pipeline behavior system for extensibility.

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
