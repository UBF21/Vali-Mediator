# Changelog

All notable changes to Vali-Mediator and its extension packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## Vali-Mediator.Resilience v1.2.4

Released on 2026-04-22

### Added

- **Unit tests for auto-discovery** — `PolicyProviderRegistrationTests` covers `RegisterResiliencePoliciesFromAssemblyContaining<T>()` and `RegisterResiliencePoliciesFromAssembly()`: provider discovery, default `Scoped` lifetime, lifetime override, null argument guards, and policy validity. Brings total test count from 87 to 97.

---

## Vali-Mediator.Resilience v1.2.3

Released on 2026-04-22

### Added

- **`RegisterResiliencePoliciesFromAssemblyContaining<T>()`** — auto-discovers and registers all `IResiliencePolicyProvider<TRequest>` implementations from the specified assembly. Eliminates manual `AddResiliencePolicyProvider<T, P>()` calls and maintains consistency with handler discovery pattern.
- **`RegisterResiliencePoliciesFromAssembly(assembly, lifetime)`** — explicit assembly-based variant of the above.

### Changed

- Policy providers are now discovered and registered automatically via assembly scan, just like handlers. Manual registration is no longer needed for most cases.

---

## Vali-Mediator.Resilience v1.2.2

Released on 2026-04-20

### Fixed

- **`ResilienceBehavior<TRequest,TResponse>` policy caching** — the resolved `ResiliencePolicy` is now cached in a static field per `TRequest`+`TResponse` combination using double-checked locking. This covers both `AddResiliencePolicy<T>` lambdas **and** class-based `IResiliencePolicyProvider<T>` providers. Previously `GetPolicy()` was called on every request, causing stateful policies (Circuit Breaker, Rate Limiter, Bulkhead, Hedge) to lose their accumulated state regardless of how the provider was registered.

---

## Vali-Mediator.Resilience v1.2.1

Released on 2026-04-20

### Fixed

- **`DelegateResiliencePolicyProvider<TRequest>`** — the `ResiliencePolicy` built by the inline lambda registered via `services.AddResiliencePolicy<T>()` is now cached after the first request using double-checked locking. Previously the factory was invoked on every call, which caused stateful policies (Circuit Breaker, Rate Limiter, Bulkhead, Hedge) to lose their accumulated state between requests.

---

## Vali-Mediator.Resilience v1.2.0

Released on 2026-04-20

### Added

- **`IResiliencePolicyProvider<TRequest>`** — new interface for declaring resilience policies in a separate class registered in DI, keeping policy configuration out of the command/query model.
- **`services.AddResiliencePolicy<TRequest>(factory)`** — inline lambda registration, no class needed for the majority of cases.
- **`services.AddResiliencePolicyProvider<TRequest, TProvider>()`** — class-based registration for providers that need injected dependencies (`IOptions`, `ILogger`, etc.).
- **`IGlobalResiliencePolicyProvider`** — fallback policy applied to every request that has no specific provider registered.
- **`services.AddGlobalResiliencePolicy(policy)`** — register a fixed global policy.
- **`services.AddGlobalResiliencePolicy(factory)`** — register a global policy factory that receives the request instance (useful for type-based differentiation).
- **`RateLimiterOptions.PartitionKeyResolver`** — `Func<object, string>` that enables per-partition rate limiting (e.g. per user ID or IP). Each unique key gets its own independent counter.

### Changed

- **`ResilienceBehavior<TRequest,TResponse>`** policy resolution order:
  1. `IResiliencePolicyProvider<TRequest>` (DI-registered, preferred)
  2. `IResilient` on the request (backward compat, deprecated)
  3. `IGlobalResiliencePolicyProvider` (fallback)

### Deprecated

- **`IResilient`** — marked `[Obsolete]`. Putting a `ResiliencePolicy` property directly on the command mixes infrastructure with domain data. Use `services.AddResiliencePolicy<TRequest>()` instead. The interface remains functional for backward compatibility.

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
