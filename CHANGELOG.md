# Changelog

All notable changes to Vali-Mediator and its extension packages are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.0.0] — Vali-Mediator core · [2.0.0] — extension packages (Unreleased)

### Compatibility

| Package | Version | Requires |
|---|---|---|
| Vali-Mediator (core) | **3.0.0** | — |
| Vali-Mediator.AspNetCore / Caching / Idempotency / Observability / Resilience | **2.0.0** | Vali-Mediator **>= 3.0.0** |

The extension packages call `IPipelineBehavior.Handle(..., Func<CancellationToken, Task<T>> next)`, so they only work with core 3.x. Use `[3.0.0, 4.0.0)` as the recommended range in consumers. See [docs/MIGRACION-3.0.md](docs/MIGRACION-3.0.md) for the 2.x → 3.0 guide.

### Target frameworks

- **Added `net10.0` target** to Vali-Mediator, AspNetCore, Caching, Idempotency, Observability and Resilience (now `net7.0;net8.0;net9.0;net10.0`). Unit tests run on all four frameworks. `global.json` uses `rollForward: latestMajor`, so the newest installed SDK (10.x) builds every target.

### Core-wide changes made during the 3.0 stabilization

#### Breaking

- **Vali-Mediator (core) — `next` in pipeline behaviors receives a `CancellationToken`.** `IPipelineBehavior<TRequest, TResponse>.Handle` now takes `Func<CancellationToken, Task<TResponse>> next` and `IPipelineBehavior<TRequest>.Handle` takes `Func<CancellationToken, Task> next`. The mediator chains the effective token down to the handler, so a behavior that cancels on its own (e.g. a timeout) now really cancels the handler. **Migration:** change the parameter type and call `next(cancellationToken)` (or a linked token); lambdas that ignore the token become `_ => ...`. Requires a new major version of the core; Caching, Idempotency, Observability and Resilience must be republished against it.

#### Fixed

- **Vali-Mediator (core) — `TimeoutBehavior` cancels the handler on timeout.** It creates a linked `CancellationTokenSource` with `CancelAfter(timeout)` and passes its token to `next`; the handler observes it, no timer is left running, and caller cancellation surfaces as `OperationCanceledException` while a real timeout still throws `TimeoutException`. Handlers that ignore the token still time out promptly.

#### Changed

- **Vali-Mediator.Resilience — BREAKING: new execution order.** The order is now **Fallback → Chaos → RateLimiter → Retry → Timeout → Circuit Breaker → Bulkhead → Hedge → delegate**. Previously Chaos and RateLimiter ran inside Retry and Circuit Breaker, so a rate-limit rejection was retried, consumed one permit per attempt and counted as a circuit-breaker failure. Now they are evaluated once per logical call. Timeout remains per attempt.

- **Vali-Mediator.Resilience — `ResilienceBehavior` resolves the policy per request.** The policy was previously cached statically per `<TRequest, TResponse>` (first request won, even `null`), so `IResiliencePolicyProvider<T>.GetPolicy(request)` and factories such as `Retry(req is IQuery ? 3 : 1)` ignored later requests. `GetPolicy` is now called on every request and `null` (no resilience) is never cached. `AddResiliencePolicy<T>(factory)` also runs its factory on every request. **Behavior change:** providers/factories that build a new `ResiliencePolicy` on each call lose circuit-breaker, bulkhead and rate-limiter state between calls; build the policy once and return the same instance to keep it.

#### Fixed

- **Vali-Mediator.Idempotency — per-key locks no longer leak:** locks are ref-counted and removed when the last waiter leaves (the in-process lock still does not coordinate multiple instances of a distributed store).
- **Vali-Mediator.Idempotency — request type is part of the store key:** two request types sharing an `IdempotencyKey` no longer overwrite each other or deserialize the wrong type. **Behavior change:** the stored key is now `<TRequest full name>:<IdempotencyKey>`, so entries written by earlier versions are not found (the handler runs once more); a stored `ResponseTypeName` that differs from the expected response type is treated as a miss.
- **Vali-Mediator.Resilience — Fallback** no longer swallows the caller's `OperationCanceledException`.
- **Vali-Mediator.Resilience — Bulkhead** now honors `MaxQueuedCalls`: callers wait for a slot when the queue has room (indefinitely with an infinite `QueueTimeout`) and are rejected immediately when it is full or `MaxQueuedCalls` is 0.
- **Vali-Mediator.Resilience — Circuit Breaker:** `HalfOpenMaxAttempts` is exact (was +1); the failure window is cleared when the circuit closes; `OnHalfOpen`/`OnClose` fire once per transition (`OnClose` is now awaited; `OnOpen` also fires when a failed `IResult` opens the circuit); `_openedAt` is read atomically; caller cancellation and bulkhead rejections no longer count as failures and give the HalfOpen probe back.
- **Vali-Mediator.Resilience — Retry:** exponential/linear/jitter backoff no longer throws `OverflowException` with many retries (delay is clamped to `MaxDelay` before building the `TimeSpan`).
- **Vali-Mediator.Resilience — Pessimistic timeout** cancels its timer when the operation finishes first and no longer allocates an unused `CancellationTokenSource`.

### Vali-Mediator (core) 3.0.0

#### Fixed
- `AddRequestBehavior<T>()` / `AddDispatchBehavior<T>()` with a closed behavior (`LoggingBehavior<Ping, string>`) made `BuildServiceProvider` throw (open service type with closed implementation). A closed type is now registered for each closed `IPipelineBehavior` interface it implements; a type that does not implement it throws a clear `ArgumentException`. New `AddRequestBehavior(Type, lifetime)` / `AddDispatchBehavior(Type, lifetime)` overloads take an open-generic type (`typeof(LoggingBehavior<,>)`), which is what the docs now show (the previous `AddRequestBehavior<LoggingBehavior<,>>()` examples never compiled).
- A pre/post processor registered explicitly (`AddPreProcessor`, `AddPostProcessor`, `AddRequestPreProcessor`, `AddRequestPostProcessor`) and also found by the assembly scan was registered twice and ran twice. The explicit registration now skips a (service, implementation) pair that is already present.
- **Vali-Mediator.Resilience:** new `ResiliencePolicy.ExecuteForRequestAsync(request, ...)` lets `RateLimiterOptions.PartitionKeyResolver` work when a policy is used directly (through the mediator it was already automatic).

#### Breaking
- `Publish<T>` now runs handlers registered for the runtime type **and** for the static type (deduplicated by handler class). Handlers registered only for a base type/`INotification` no longer get skipped when the runtime type differs (AUD-C-02).
- Scanning: registering the same assembly again (same `AddValiMediator` call or a second call on the same collection) keeps a single registration and the **last** lifetime wins (AUD-C-06).
- `Result.Fail("x", ErrorType.None)` keeps `ErrorType.None`; only `default(Result)` / `default(Result<T>)` report `ErrorType.Failure` (AUD-C-08).
- `TimeoutBehavior` throws `TimeoutException` (with the handler exception as `InnerException`) when the timeout expired and the handler failed with any exception, not only `OperationCanceledException` (AUD-C-09).

#### Added
- `IValiMediator.SendAll(requests, maxDegreeOfParallelism, cancellationToken)` (default interface member): bounded concurrency, results keep request order; `maxDegreeOfParallelism < 1` throws `ArgumentOutOfRangeException` (M-05).

#### Changed
- Send, SendOrDefault, fire-and-forget, streams and Publish use typed dispatchers cached per message type: no `MakeGenericType`, `MethodInfo.Invoke` or `object[]` per call; pre/post processors are not materialised when none are registered (AUD-F-01, F-02, F-03, O-18, O-06). Removed the internal `ReflectionCache`.

#### Fixed
- A handler that ignores its token and fails after a timeout no longer produces an unobserved task exception (AUD-C-09).
- Tests: removed tight timing margins (AUD-T-02, T-03); added coverage for streaming, `INotificationFilter`, dead-letter queue, `SendOrDefault`, `SendAll`, publish strategies and concurrent `Send` (AUD-T-04, T-06).

#### Fixed (earlier stages)
- `Publish<INotification>(x)` resolves handlers by the runtime type (B-01).
- Handler exceptions thrown synchronously reach the caller with their original type and stack (no `TargetInvocationException`) (M-03).
- Every handler interface a class implements is registered (was: only the first); open-generic types are skipped when scanning, `ReflectionTypeLoadException` uses the types that did load, and re-scanning does not duplicate handlers (M-01, M-02).
- `Result<T>.Fail(errors, errorType)` honours `errorType` and rejects a null dictionary (M-04).
- `default(Result)` / `default(Result<T>)` are an explicit "not initialized" failure instead of `Error = null` (B-10).
- `SendAll` documents the shared DI scope; the dead-letter queue is bounded by `maxEntries` (M-05, B-03).

### Vali-Mediator.Resilience 2.0.0

#### Added
- `ResiliencePolicyBuilder.WithSharedState(key)`: bulkhead, rate limiter and circuit breaker shared by name between separately built policies (AUD-R-02). Capped at 10 000 keys.
- `RateLimiterOptions.MaxPartitions` (default 10 000) with a shared overflow limiter for extra keys (AUD-S-04).
- `ChaosOptions.InjectPerAttempt` to inject faults inside Retry/Timeout/Circuit Breaker (AUD-R-06).
- Build-time validation of every option (`ArgumentOutOfRangeException`) (O-19).
- `FallbackOptions.FallbackOnResultPredicate` now works (it was documented but ignored).
- XML documentation for the whole public API (AUD-P-10).

#### Changed
- Pipeline is composed from one middleware per policy; the 8-parameter pipeline constructor is gone (O-03/O-04).
- Hedge returns the winner immediately and no longer awaits losing attempts (AUD-R-01); `OnHedge` receives its own context copy (AUD-R-07).
- Rate limiter waits for the next permit instead of polling every 10 ms (AUD-S-08), uses a monotonic clock and overflow-safe arithmetic (AUD-R-05).
- `TokensPerInterval = 0`, `BucketCapacity = 0` and `PermitLimit = 0` remain valid (no refill / reject everything).

#### Fixed
- Circuit breaker: HalfOpen probe released when `OnHalfOpen` throws or a nested `CircuitOpenException` escapes; lost probes are replaced after `BreakDuration` (AUD-R-03); late results from earlier states are ignored via state epochs (AUD-R-04, AUD-R-07).
- Circuit breaker state is now guarded by a single lock (atomic transitions, no torn reads).

#### Breaking
- Options outside their valid range now throw at `Build()` instead of misbehaving at run time.
- Hedge: abandoned attempts keep running if the operation ignores its token.
- Per-request policy factories that relied on Bulkhead/RateLimiter limits must call `WithSharedState(key)` (or return one shared instance).

#### Fixed (earlier stages)
- Hedge rewritten: when every attempt fails the last exception is thrown (was: `default(T)` returned silently), no CPU spinning while attempts are in flight, `HedgeDelay` is respected after a failure, losers are cancelled (A-01, A-02, B-08, B-09).
- Rate limiter keeps fractional tokens (rate no longer under-delivers) and idle partitions are released (M-14, M-15). Removed the unused `EffectiveRegistry` on the builder (B-12).

### Vali-Mediator.Caching 2.0.0

#### Added
- `InMemoryCacheOptions.MaxKeyLength` (512), `MaxGroups` (10 000), `MaxKeysPerGroup` (10 000) and `TimeProvider`; all limits are validated (`ArgumentOutOfRangeException` when <= 0). (AUD-S-05, AUD-C-10, O-08)
- `CachingOptions.CoalescingWaitTimeout` (30 s, `Timeout.InfiniteTimeSpan` allowed) and `services.AddCachingOptions(...)`; `CachingBehavior` takes an optional `CachingOptions`. (AUD-C-05)
- Package reference to `Microsoft.Bcl.TimeProvider` for net7.0 only, so `TimeProvider` is available on every target. (O-08)

#### Changed
- `InMemoryCacheOptions.MaxEntries` default raised from 1 000 to 10 000. (AUD-S-05)
- `InMemoryCacheOptions.CleanupInterval` now drives a lazy sweep of expired entries on writes (at most once per interval); it was previously reserved and unused. (AUD-S-05)
- `InMemoryCacheStore` rewritten around a single lock, a dictionary and an LRU linked list: eviction is O(1) instead of an O(n) scan per insert and `MaxEntries` is a strict bound under concurrency. (AUD-S-05, AUD-C-10)
- A key belongs to at most one group; registering it under another group moves it. (AUD-C-04)
- Coalescing: concurrent misses share the leader's outcome, failed `Result` values included (still never cached); waiters give up after `CoalescingWaitTimeout` and run the handler themselves, retry when the leader is cancelled and observe the leader's exception. (AUD-C-05, O-05)
- XML docs: `ICacheable.CacheKey` (user/tenant in the key, shared instance), `ICacheStore` and `IGroupAwareCacheStore` contracts (O-09, AUD-S-06). Caching docs (en/es) now describe the real options.

#### Fixed
- Group index could lose track of a key when an old entry was untracked after a new one was registered; membership is now updated atomically with the entry. (AUD-C-04)
- `RegisterKeyInGroupAsync` on a key that was already evicted left an orphan in the group index; it is now a no-op. (AUD-S-05, AUD-C-10)
- Keys longer than `MaxKeyLength` are never stored; keys that cannot be indexed under a group because of `MaxGroups`/`MaxKeysPerGroup` are dropped instead of being left uninvalidatable. (AUD-S-05)
- Time-based tests use a fake clock instead of `Task.Delay`. (AUD-T-02)

#### Breaking
- None in the public API. Behavioral: eviction order is now strictly LRU, and a key is registered under a single group.

#### Fixed (earlier stages)
- Failed `Result` values are no longer cached, and a failed command no longer invalidates the cache (A-04, B-05).
- Concurrent misses on the same key are coalesced instead of running the handler N times (M-18).

### Vali-Mediator.Idempotency 2.0.0

#### Added
- **Atomic reservation across instances.** `IIdempotencyStore` gains three default members (`SupportsReservation`, `TryReserveAsync`, `ReleaseReservationAsync`); a store that opts in makes instances sharing it run the same key once. Before, two API instances receiving the same `Idempotency-Key` at the same time both executed the handler (found with two real processes on a shared Redis: two orders for one key). New `IdempotencyOptions.ReservationLease` / `ReservationWaitTimeout` / `ReservationPollInterval`, new `IdempotencyInProgressException`; `InMemoryIdempotencyStore` implements the contract. Stores that do not opt in behave exactly as before.
- `IIdempotent.IdempotencyScope` (default interface member, `null` by default): isolates keys per user/tenant (AUD-S-01).
- Request payload fingerprint (SHA-256): reusing a key with a different payload returns `Conflict` for `IResult` responses or throws `IdempotencyConflictException` (AUD-S-01).
- `IdempotencyOptions` (`MaxKeyLength` = 256, `VerifyRequestFingerprint` = true) and `AddIdempotencyOptions(...)` (AUD-S-02).
- `InMemoryIdempotencyStoreOptions` (`MaxEntries` = 10,000, `DefaultExpiration` = 24 h) and `AddInMemoryIdempotencyStore(Action<...>)` (AUD-S-02).
- `TimeProvider` support in the behavior and the in-memory store; `Microsoft.Bcl.TimeProvider` is referenced on net7.0 only.

#### Changed
- The store key now includes the length-prefixed scope: `Type#len:scope#key` (breaking for entries written by 2.x/earlier 3.x builds, which are simply treated as misses).
- Response type names are compared without assembly versions, so entries survive package upgrades (AUD-C-01).
- `InMemoryIdempotencyStore` is bounded: oldest-first eviction, default expiration for entries without one, O(1) writes (AUD-S-02).
- The JSON converter for `Result`/`Result<T>` keeps the original `ErrorType`, including on validation failures (AUD-C-08).

#### Fixed
- Empty, whitespace or over-long idempotency keys and scopes are rejected with `ArgumentException` (AUD-S-02).
- Failed `Result` values are never stored, so transient errors are not replayed (A-05).

#### Fixed (earlier stages)
- `Result` / `Result<T>` now survive the JSON round-trip through `JsonIdempotencySerializer` (a custom converter replaces the default-struct result that replayed every hit as a failure) (A-03).
- Per-key locks are ref-counted and released (no leak) and the request type is part of the key (M-16, M-17; the key format is superseded by the scoped format above).

### Vali-Mediator.Observability 2.0.0

#### Added
- `ObservabilityOptions` with `IncludeExceptionMessage` (default `false`) and `services.AddObservability(Action<ObservabilityOptions>)` overload (AUD-S-07).
- `IMetricsCollector.RecordObserverError(observerType, hook, exception)` default interface member, invoked whenever an observer throws; `ConsoleMetricsCollector` prints it without the exception message (AUD-C-03).
- Tests for the `Vali-Mediator` `ActivitySource` (span, tags, error status, `observer.error` event), `ConsoleMetricsCollector`, `ConsoleLoggingObserver`, multiple observers and dispatch failures (AUD-T-05).

#### Changed
- By default the activity error status, the `observer.error` event and `ConsoleLoggingObserver` output expose only the exception type, not its message (AUD-S-07). Set `IncludeExceptionMessage = true` to restore the previous behavior.
- `ObservabilityBehavior`, `ObservabilityDispatchBehavior` and `ConsoleLoggingObserver` accept an optional `ObservabilityOptions` constructor argument (source compatible).

#### Fixed
- Observer errors were lost when no `ActivityListener` was attached; they now also reach the metrics collector (AUD-C-03).
- A throwing metrics collector cannot turn an isolated observer error into a request failure.

#### Fixed (earlier stages)
- A throwing observer no longer counts as a request failure or replaces the handler's exception; `OnStarted` errors no longer abort the request (M-21, M-22).

### Vali-Mediator.AspNetCore 2.0.0

#### Added
- `ResultHttpOptions` (`ExposeErrorDetails`, default `false`) and overloads `ToActionResult(options)` / `ToHttpResult(options)` for `Result` and `Result<T>`. (AUD-S-03)
- New test project `Vali-Mediator.AspNetCore.Tests` covering every `ErrorType` for MVC and Minimal API, validation problems, options and null guards. (AUD-T-01)

#### Changed
- `ToHttpResult` now returns the same status, `title` and body as `ToActionResult` for Unauthorized (401), Forbidden (403), non-generic Validation (400) and Failure (500); previously some returned an empty body or a different problem shape. (B-04)
- Non-generic `Validation` with no structured errors returns a `ProblemDetails` titled "Validation Failed" in both styles.
- XML docs completed for the public API. (AUD-P-10)

#### Breaking
- `Failure` (HTTP 500) responses no longer include `Result.Error` in `ProblemDetails.Detail` by default; they use "An unexpected error occurred." Set `ExposeErrorDetails = true` to restore the previous behaviour. (AUD-S-03)

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
