# Vali-Mediator.AspNetCore

ASP.NET Core integration for [Vali-Mediator](https://github.com/UBF21/Vali-Mediator). Maps `Result<T>` and `Result` to HTTP responses for both MVC controllers and Minimal API.

## Installation

```bash
dotnet add package Vali-Mediator.AspNetCore
```

## What it does

Provides two extension methods on `Result<T>` and `Result`:

| Method | Use case |
|--------|----------|
| `ToActionResult()` | MVC controllers → returns `IActionResult` |
| `ToHttpResult()` | Minimal API → returns `IResult` |

## ErrorType → HTTP Status Code Mapping

| ErrorType | HTTP Status |
|-----------|-------------|
| `None` (success) | 200 OK (or 204 No Content for non-generic `Result`) |
| `Validation` | 400 Bad Request |
| `NotFound` | 404 Not Found |
| `Conflict` | 409 Conflict |
| `Unauthorized` | 401 Unauthorized |
| `Forbidden` | 403 Forbidden |
| `Failure` | 500 Internal Server Error |

When `Result<T>` carries structured `ValidationErrors` (a dictionary keyed by property name), the 400 response uses `ValidationProblemDetails` for a structured error body.

## Usage: MVC Controllers

```csharp
using Vali_Mediator.AspNetCore;
using Vali_Mediator.Core.Result;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IValiMediator _mediator;

    public OrdersController(IValiMediator mediator) => _mediator = mediator;

    // Result<T> example
    [HttpPost]
    public async Task<IActionResult> PlaceOrder(PlaceOrderCommand command)
    {
        Result<string> result = await _mediator.Send(command);
        return result.ToActionResult(); // 200 OK with value, or error response
    }

    // Non-generic Result example (void handler)
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOrder(string id)
    {
        Result result = await _mediator.Send(new DeleteOrderCommand { Id = id });
        return result.ToActionResult(); // 204 No Content on success, or error response
    }
}
```

## Usage: Minimal API

```csharp
using Vali_Mediator.AspNetCore;
using Vali_Mediator.Core.Result;

var app = builder.Build();

// Result<T> example
app.MapPost("/api/orders", async (PlaceOrderCommand command, IValiMediator mediator) =>
{
    Result<string> result = await mediator.Send(command);
    return result.ToHttpResult(); // IResult: 200 OK, or structured error
});

// Non-generic Result example
app.MapDelete("/api/orders/{id}", async (string id, IValiMediator mediator) =>
{
    Result result = await mediator.Send(new DeleteOrderCommand { Id = id });
    return result.ToHttpResult(); // IResult: 204 No Content, or structured error
});
```

## Validation Errors

When a `Result<T>` is created with structured validation errors via `Result<T>.Fail(Dictionary<string, List<string>> errors, ErrorType.Validation)`, the 400 response returns a `ValidationProblemDetails` body:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Name": ["'Name' must not be empty."],
    "Price": ["'Price' must be greater than 0."]
  }
}
```

---

## Donations

If Vali-Mediator is useful to you, consider supporting its development:

- **Latin America** — [MercadoPago](https://link.mercadopago.com.pe/felipermm)
- **International** — [PayPal](https://paypal.me/felipeRMM?country.x=PE&locale.x=es_XC)

---

## License

[Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## Contributions

Issues and pull requests are welcome on [GitHub](https://github.com/UBF21/Vali-Mediator).
