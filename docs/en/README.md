# Vali-Mediator Documentation (English)

Welcome to the English documentation for **Vali-Mediator**, the lightweight mediator library for .NET 7/8/9 from the Vali ecosystem.

---

## Table of Contents

### Fundamentals

| # | Document | Description |
|---|---|---|
| 01 | [Introduction](01-introduction.md) | What Vali-Mediator is, features, philosophy, and comparison with MediatR |
| 02 | [Installation](02-installation.md) | NuGet package installation, DI setup, and `Program.cs` configuration |
| 03 | [Quick Start](03-quick-start.md) | First request in 5 minutes: model, handler, DI, endpoint |

### Core Concepts

| # | Document | Description |
|---|---|---|
| 04 | [Requests](04-requests.md) | `IRequest<T>`, `IRequest` (void), `IRequestHandler`, `SendOrDefault` |
| 05 | [Notifications](05-notifications.md) | `INotification`, `INotificationHandler`, `Priority`, `PublishStrategy` (Sequential/Parallel/ResilientParallel) |
| 06 | [Fire and Forget](06-fire-and-forget.md) | `IFireAndForget`, `IFireAndForgetHandler`, use cases |
| 07 | [Streaming](07-streaming.md) | `IStreamRequest<T>`, `IStreamRequestHandler`, `CreateStream`, pipeline bypass |

### Pipeline and Processing

| # | Document | Description |
|---|---|---|
| 08 | [Pipeline Behaviors](08-pipeline-behaviors.md) | `IPipelineBehavior<TRequest,TResponse>`, `IPipelineBehavior<TRequest>`, registration order, logging/timing examples |
| 09 | [Processors](09-processors.md) | `IPreProcessor`, `IPostProcessor` (both variants), auto-discovery from assembly |

### Result Type

| # | Document | Description |
|---|---|---|
| 10 | [Result](10-result.md) | `Result<T>`: Ok/Fail/Map/Bind/MapAsync/BindAsync/Tap/OnFailure/Match, `ValidationErrors`, implicit operator; `Result` (non-generic); `ErrorType` enum |

### Advanced Features

| # | Document | Description |
|---|---|---|
| 11 | [Compensation](11-compensation.md) | `ICompensable`, `Compensable` base class, Saga pattern, rollback example |
| 12 | [Dependency Injection](12-dependency-injection.md) | `AddValiMediator`, `RegisterServicesFromAssembly`, `RegisterServicesFromAssemblyContaining<T>`, behavior registration, `ServiceLifetime` |
| 13 | [ASP.NET Core Integration](13-aspnetcore-integration.md) | `Vali-Mediator.AspNetCore`: `ToActionResult()`, `ToHttpResult()`, `ErrorType`→HTTP mapping, controller and Minimal API examples |

### Extension Packages

| # | Document | Description |
|---|---|---|
| 14 | [Resilience](14-resilience.md) | `Vali-Mediator.Resilience`: Retry, Circuit Breaker, Timeout, Bulkhead, Hedge, Rate Limiter, Chaos, Fallback |
| 15 | [Caching](15-caching.md) | `Vali-Mediator.Caching`: `ICacheable`, `IInvalidatesCache`, `ICacheStore`, in-memory store, group invalidation |
| 16 | [Observability](16-observability.md) | `Vali-Mediator.Observability`: `ActivitySource`, OpenTelemetry tracing, `IMetricsCollector`, `IRequestObserver` |
| 17 | [Idempotency](17-idempotency.md) | `Vali-Mediator.Idempotency`: `IIdempotent`, `IIdempotencyStore`, `IdempotencyBehavior`, replay protection |

---

## Quick Reading Guide

### I am new to Vali-Mediator

1. Read [Introduction](01-introduction.md) to understand the purpose and philosophy
2. Follow [Installation](02-installation.md) to add the package
3. Do the [Quick Start](03-quick-start.md) to get something working in minutes

### I want to handle commands and queries

1. [Requests](04-requests.md) — `IRequest<T>` and `IRequestHandler<TRequest, TResponse>`
2. [Pipeline Behaviors](08-pipeline-behaviors.md) — add cross-cutting concerns
3. [Result](10-result.md) — return typed results without exceptions

### I want pub/sub events

1. [Notifications](05-notifications.md) — `INotification` and multiple handlers
2. [Processors](09-processors.md) — pre/post processors for dispatch types

### I want background/side-effect operations

1. [Fire and Forget](06-fire-and-forget.md) — `IFireAndForget` commands
2. [Compensation](11-compensation.md) — rollback on failure (Saga pattern)

### I want to stream data

1. [Streaming](07-streaming.md) — `IStreamRequest<T>` and `IAsyncEnumerable<T>`

### I want to integrate with ASP.NET Core

1. [Installation](02-installation.md) — package setup
2. [ASP.NET Core Integration](13-aspnetcore-integration.md) — result to HTTP response mapping

### I want resilience (retry, circuit breaker, etc.)

1. [Resilience](14-resilience.md) — `ResiliencePolicy` fluent builder, all policy types

### I want caching without dependencies

1. [Caching](15-caching.md) — `ICacheable` on requests, invalidation, custom stores

### I want distributed tracing and metrics

1. [Observability](16-observability.md) — `ActivitySource`, OpenTelemetry, metrics, observers

### I want to prevent duplicate request processing

1. [Idempotency](17-idempotency.md) — `IIdempotent` on requests, custom stores

---

## NuGet Packages

| Package | Command |
|---|---|
| Core | `dotnet add package Vali-Mediator` |
| ASP.NET Core | `dotnet add package Vali-Mediator.AspNetCore` |
| Resilience | `dotnet add package Vali-Mediator.Resilience` |
| Caching | `dotnet add package Vali-Mediator.Caching` |
| Observability | `dotnet add package Vali-Mediator.Observability` |
| Idempotency | `dotnet add package Vali-Mediator.Idempotency` |

**Target frameworks:** .NET 7, .NET 8, .NET 9

**Only dependency:** `Microsoft.Extensions.DependencyInjection.Abstractions`

---

## Additional Resources

- **GitHub Repository:** [Vali-Mediator](https://github.com/UBF21/Vali-Mediator)
- **Vali-Validation:** [Vali-Validation](https://github.com/feliperafaelmontenegro/Vali-Validation)
- **NuGet:** [nuget.org/packages/Vali-Mediator](https://www.nuget.org/packages/Vali-Mediator)
