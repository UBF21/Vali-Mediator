# Changelog

All notable changes to Vali-Mediator and its extension packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## Extension Packages v1.1.0

Released on 2026-04-13

### Changed (All Extension Packages)

- **Package structure**: All extension packages (`Vali-Mediator.AspNetCore`, `Vali-Mediator.Resilience`, `Vali-Mediator.Caching`, `Vali-Mediator.Observability`, `Vali-Mediator.Idempotency`) now depend on `Vali-Mediator` via NuGet `PackageReference` instead of local `ProjectReference`.
  - **Benefit**: Cleaner dependency management, independent package versioning, and improved separation of concerns.
  - **Impact**: Fully backward compatible — no API changes.

### Affected Packages

- `Vali-Mediator.AspNetCore` → v1.1.0
- `Vali-Mediator.Resilience` → v1.1.0
- `Vali-Mediator.Caching` → v1.1.0
- `Vali-Mediator.Observability` → v1.1.0
- `Vali-Mediator.Idempotency` → v1.1.0

---

## Vali-Mediator v2.0.0

Released on 2025-12-XX (reference version from project)

### Core Features

- **Result Pattern**: Readonly struct `Result<T>` and `Result` with functional operations (`Map`, `Bind`, `Tap`, `Match`, etc.)
- **CQRS Support**: `IRequest<T>`, `INotification`, `IFireAndForget`, `IStreamRequest<T>`
- **Pipeline Architecture**: Pre/post-processors, open-generic behaviors, proper execution order
- **Advanced Publishing**: Sequential, Parallel, and ResilientParallel strategies
- **Streaming**: `CreateStream()` for async enumerable responses
- **Error Handling**: Structured error types, `HandlerNotFoundException`, typed exceptions

### Extension Packages v1.0.0+

#### Vali-Mediator.AspNetCore v1.0.1+
- Maps `Result<T>` → HTTP status codes (200, 400, 404, 409, 401, 403, 500)
- `ToActionResult()` for MVC and `ToHttpResult()` for Minimal API
- Structured validation errors as `ValidationProblemDetails`

#### Vali-Mediator.Resilience v1.0.1+
- Policies: Retry, Circuit Breaker, Timeout, Bulkhead, Hedge, Rate Limiter, Chaos, Fallback
- Fluent builder API: `ResiliencePolicy.Create()`
- `IResilient` interface for handler-level policies
- Dead Letter Queue for failed requests

#### Vali-Mediator.Caching v1.0.1+
- `ICacheable` for request-level caching
- `IInvalidatesCache` for explicit invalidation
- Pluggable `ICacheStore` abstraction
- In-memory cache store with expiry and group-based invalidation

#### Vali-Mediator.Observability v1.0.1+
- OpenTelemetry-compatible `ActivitySource` ("Vali-Mediator")
- `IRequestObserver` lifecycle hooks
- Pluggable `IMetricsCollector`
- Console diagnostics support

#### Vali-Mediator.Idempotency v1.0.1+
- `IIdempotent` marker for request deduplication
- `IIdempotencyStore` abstraction with in-memory implementation
- JSON serialization support
- Per-key SemaphoreSlim locking for concurrent requests

---

## Notes

- **Target Frameworks**: .NET 7.0, 8.0, 9.0
- **Dependencies**: Only `Microsoft.Extensions.DependencyInjection.Abstractions` + framework features
- **License**: Apache 2.0
- **Repository**: [github.com/UBF21/Vali-Mediator](https://github.com/UBF21/Vali-Mediator)
