# Introduction to Vali-Mediator

## What is Vali-Mediator?

Vali-Mediator is a lightweight, zero-dependency mediator library for .NET 7, 8, and 9. It implements the Mediator and CQRS patterns, enabling you to decouple the sender of a request from its handler through a central mediator object.

The library is built from scratch with a focus on:

- **Zero external dependencies** — only `Microsoft.Extensions.DependencyInjection.Abstractions`
- **Full async/await support** with proper `CancellationToken` propagation
- **First-class Result type** — `Result<T>` and `Result` for error handling without exceptions
- **Complete pipeline** — behaviors, pre/post processors, and compensation flows
- **Modern .NET** — designed for .NET 7+ with nullable reference types enabled

## Core Features

| Feature | Description |
|---|---|
| Requests | `IRequest<T>` / `IRequest` — query/command pattern with a single handler |
| Notifications | `INotification` — publish-subscribe with multiple handlers and priority ordering |
| Fire and Forget | `IFireAndForget` — one-way commands for side effects (emails, queuing, logging) |
| Streaming | `IStreamRequest<T>` — async streaming via `IAsyncEnumerable<T>` |
| Pipeline Behaviors | Cross-cutting concerns (logging, validation, timing) applied before/after handlers |
| Processors | `IPreProcessor` / `IPostProcessor` — lighter alternative to full behaviors |
| Result type | `Result<T>` / `Result` — typed success/failure without exceptions |
| Compensation | `ICompensable` / `Compensable` — Saga-pattern rollback on failure |

## Philosophy

Vali-Mediator is designed around three principles:

**1. Separation of concerns**

Business logic lives in handlers. Controllers, endpoints, and application services only know about the mediator — they do not depend on repositories, services, or domain objects directly.

**2. Explicit error handling**

Instead of throwing exceptions for expected business failures (e.g., "product not found", "email already taken"), handlers return `Result<T>`. This makes the failure path explicit and type-safe.

**3. Composable pipeline**

Cross-cutting concerns — logging, validation, authorization, caching — are implemented as pipeline behaviors and stacked around every request handler. Adding a new concern does not require changing existing handlers.

## Architecture Overview

```
Client (Controller / Endpoint)
    │
    ▼
IValiMediator
    │
    ├── Send<TResponse>(IRequest<TResponse>)
    │       │
    │       ├── [Behavior 1 (outermost)]
    │       ├── [Behavior 2]
    │       ├── [PreProcessors]
    │       ├── IRequestHandler<TRequest, TResponse>
    │       └── [PostProcessors]
    │
    ├── Publish<TNotification>(notification, strategy)
    │       │
    │       ├── [Dispatch Behaviors]
    │       └── INotificationHandler<TNotification> × N (ordered by Priority)
    │
    ├── Send(IFireAndForget)
    │       │
    │       ├── [Dispatch Behaviors]
    │       └── IFireAndForgetHandler<TFireAndForget>
    │
    └── CreateStream<TResponse>(IStreamRequest<TResponse>)
            │
            └── IStreamRequestHandler<TRequest, TResponse>
                (no behaviors — direct call)
```

## Comparison with MediatR

| Feature | Vali-Mediator | MediatR |
|---|---|---|
| External dependencies (core) | DI Abstractions only | None |
| Built-in Result type | Yes — `Result<T>`, `Result` | No (use third-party) |
| Streaming | `IStreamRequest<T>` | `IStreamRequest<T>` |
| Fire and forget | `IFireAndForget` (with pipeline) | `IRequest` with `Unit` |
| Compensation (Saga) | Built-in `ICompensable` | Not built-in |
| Notification priority | `INotificationHandler.Priority` | Not built-in |
| Publish strategies | Sequential, Parallel, ResilientParallel | Sequential, Parallel |
| Pre/Post processors | Auto-discovered from assembly | Manual registration |
| Target frameworks | .NET 7/8/9 | .NET Standard 2.0+ |

## When to Use Vali-Mediator

Vali-Mediator is a good fit when:

- You are building a **clean architecture** or **vertical slices** application
- You want **CQRS** without a full event sourcing infrastructure
- You prefer **Result types** over exception-driven error flow
- You need **pub/sub notifications** within the same process
- You want **streaming** (reports, large datasets) through the same abstraction
- You are already in the **Vali ecosystem** (Vali-Validation)

## Version History

- **v1.x** — Requests, notifications, fire-and-forget, streaming, pipeline behaviors, processors, compensation
- **v2.0** — `Result<T>` functional methods (Map, Bind, Tap, OnFailure), `Result` (non-generic), `ValidationErrors`, `SendOrDefault`, `ResilientParallel`, `RegisterServicesFromAssemblyContaining<T>`, `AddRequestBehavior<T>` / `AddDispatchBehavior<T>`

## Next Steps

- **[Installation](02-installation.md)** — Add the package and configure DI
- **[Quick Start](03-quick-start.md)** — First request in 5 minutes
- **[Result](10-result.md)** — Learn the built-in result type
