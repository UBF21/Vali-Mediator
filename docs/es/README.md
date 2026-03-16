# Documentación de Vali-Mediator (Español)

Bienvenido a la documentación en español de **Vali-Mediator**, la biblioteca mediator ligera para .NET 7/8/9 del ecosistema Vali.

---

## Tabla de Contenidos

### Fundamentos

| # | Documento | Descripción |
|---|---|---|
| 01 | [Introducción](01-introduccion.md) | Qué es Vali-Mediator, características, filosofía y comparación con MediatR |
| 02 | [Instalación](02-instalacion.md) | Instalación del paquete NuGet, configuración de DI y `Program.cs` |
| 03 | [Inicio Rápido](03-inicio-rapido.md) | Primera petición en 5 minutos: modelo, handler, DI, endpoint |

### Conceptos Fundamentales

| # | Documento | Descripción |
|---|---|---|
| 04 | [Peticiones](04-peticiones.md) | `IRequest<T>`, `IRequest` (void), `IRequestHandler`, `SendOrDefault` |
| 05 | [Notificaciones](05-notificaciones.md) | `INotification`, `INotificationHandler`, `Priority`, `PublishStrategy` (Sequential/Parallel/ResilientParallel) |
| 06 | [Fire and Forget](06-fire-and-forget.md) | `IFireAndForget`, `IFireAndForgetHandler`, casos de uso |
| 07 | [Streaming](07-streaming.md) | `IStreamRequest<T>`, `IStreamRequestHandler`, `CreateStream`, bypass del pipeline |

### Pipeline y Procesamiento

| # | Documento | Descripción |
|---|---|---|
| 08 | [Pipeline Behaviors](08-pipeline-behaviors.md) | `IPipelineBehavior<TRequest,TResponse>`, `IPipelineBehavior<TRequest>`, orden de registro, ejemplos de logging y timing |
| 09 | [Procesadores](09-procesadores.md) | `IPreProcessor`, `IPostProcessor` (ambas variantes), auto-descubrimiento desde el assembly |

### Tipo Result

| # | Documento | Descripción |
|---|---|---|
| 10 | [Result](10-resultado.md) | `Result<T>`: Ok/Fail/Map/Bind/MapAsync/BindAsync/Tap/OnFailure/Match, `ValidationErrors`, operador implícito; `Result` (no genérico); enum `ErrorType` |

### Características Avanzadas

| # | Documento | Descripción |
|---|---|---|
| 11 | [Compensación](11-compensacion.md) | `ICompensable`, clase base `Compensable`, patrón Saga, ejemplo de rollback |
| 12 | [Inyección de Dependencias](12-inyeccion-dependencias.md) | `AddValiMediator`, `RegisterServicesFromAssembly`, `RegisterServicesFromAssemblyContaining<T>`, registro de behaviors, `ServiceLifetime` |
| 13 | [Integración ASP.NET Core](13-integracion-aspnetcore.md) | `Vali-Mediator.AspNetCore`: `ToActionResult()`, `ToHttpResult()`, tabla `ErrorType`→HTTP, ejemplos de controller y Minimal API |

### Paquetes de Extensión

| # | Documento | Descripción |
|---|---|---|
| 14 | [Resiliencia](14-resiliencia.md) | `Vali-Mediator.Resilience`: Retry, Circuit Breaker, Timeout, Bulkhead, Hedge, Rate Limiter, Chaos, Fallback |
| 15 | [Caché](15-caching.md) | `Vali-Mediator.Caching`: `ICacheable`, `IInvalidatesCache`, `ICacheStore`, store en memoria, invalidación por grupo |
| 16 | [Observabilidad](16-observabilidad.md) | `Vali-Mediator.Observability`: `ActivitySource`, trazas OpenTelemetry, `IMetricsCollector`, `IRequestObserver` |
| 17 | [Idempotencia](17-idempotencia.md) | `Vali-Mediator.Idempotency`: `IIdempotent`, `IIdempotencyStore`, `IdempotencyBehavior`, protección contra duplicados |

---

## Guía de Lectura Rápida

### Soy nuevo en Vali-Mediator

1. Lee la [Introducción](01-introduccion.md) para entender el propósito y la filosofía
2. Sigue la [Instalación](02-instalacion.md) para agregar el paquete
3. Haz el [Inicio Rápido](03-inicio-rapido.md) para tener algo funcionando en minutos

### Quiero manejar comandos y consultas

1. [Peticiones](04-peticiones.md) — `IRequest<T>` e `IRequestHandler<TRequest, TResponse>`
2. [Pipeline Behaviors](08-pipeline-behaviors.md) — agregar concerns transversales
3. [Result](10-resultado.md) — devolver resultados tipados sin excepciones

### Quiero pub/sub de eventos

1. [Notificaciones](05-notificaciones.md) — `INotification` y múltiples handlers
2. [Procesadores](09-procesadores.md) — pre/post processors para tipos dispatch

### Quiero operaciones en background

1. [Fire and Forget](06-fire-and-forget.md) — comandos `IFireAndForget`
2. [Compensación](11-compensacion.md) — rollback en caso de fallo (patrón Saga)

### Quiero hacer streaming de datos

1. [Streaming](07-streaming.md) — `IStreamRequest<T>` e `IAsyncEnumerable<T>`

### Quiero integrar con ASP.NET Core

1. [Instalación](02-instalacion.md) — configuración del paquete
2. [Integración ASP.NET Core](13-integracion-aspnetcore.md) — mapeo de resultados a respuestas HTTP

### Quiero resiliencia (retry, circuit breaker, etc.)

1. [Resiliencia](14-resiliencia.md) — `ResiliencePolicy` fluent builder, todos los tipos de políticas

### Quiero caché sin dependencias externas

1. [Caché](15-caching.md) — `ICacheable` en requests, invalidación, stores personalizados

### Quiero trazas distribuidas y métricas

1. [Observabilidad](16-observabilidad.md) — `ActivitySource`, OpenTelemetry, métricas, observers

### Quiero prevenir el procesamiento duplicado de requests

1. [Idempotencia](17-idempotencia.md) — `IIdempotent` en requests, stores personalizados

---

## Paquetes NuGet

| Paquete | Comando |
|---|---|
| Core | `dotnet add package Vali-Mediator` |
| ASP.NET Core | `dotnet add package Vali-Mediator.AspNetCore` |
| Resiliencia | `dotnet add package Vali-Mediator.Resilience` |
| Caché | `dotnet add package Vali-Mediator.Caching` |
| Observabilidad | `dotnet add package Vali-Mediator.Observability` |
| Idempotencia | `dotnet add package Vali-Mediator.Idempotency` |

**Frameworks objetivo:** .NET 7, .NET 8, .NET 9

**Única dependencia:** `Microsoft.Extensions.DependencyInjection.Abstractions`

---

## Recursos Adicionales

- **Repositorio GitHub:** [Vali-Mediator](https://github.com/UBF21/Vali-Mediator)
- **Vali-Validation:** [Vali-Validation](https://github.com/feliperafaelmontenegro/Vali-Validation)
- **NuGet:** [nuget.org/packages/Vali-Mediator](https://www.nuget.org/packages/Vali-Mediator)
