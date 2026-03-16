# Introducción a Vali-Mediator

## ¿Qué es Vali-Mediator?

Vali-Mediator es una biblioteca mediator ligera y sin dependencias externas para .NET 7, 8 y 9. Implementa los patrones Mediator y CQRS, permitiendo desacoplar el emisor de una petición de su handler mediante un objeto mediator central.

La biblioteca está construida desde cero con enfoque en:

- **Cero dependencias externas** — solo `Microsoft.Extensions.DependencyInjection.Abstractions`
- **Soporte completo de async/await** con propagación correcta de `CancellationToken`
- **Tipo Result de primera clase** — `Result<T>` y `Result` para el manejo de errores sin excepciones
- **Pipeline completo** — behaviors, pre/post processors y flujos de compensación
- **.NET moderno** — diseñado para .NET 7+ con nullable reference types habilitados

## Características Principales

| Característica | Descripción |
|---|---|
| Peticiones | `IRequest<T>` / `IRequest` — patrón query/command con un único handler |
| Notificaciones | `INotification` — publish-subscribe con múltiples handlers y ordenamiento por prioridad |
| Fire and Forget | `IFireAndForget` — comandos unidireccionales para efectos secundarios (emails, colas, logging) |
| Streaming | `IStreamRequest<T>` — streaming asíncrono vía `IAsyncEnumerable<T>` |
| Pipeline Behaviors | Concerns transversales (logging, validación, timing) aplicados antes/después de los handlers |
| Processors | `IPreProcessor` / `IPostProcessor` — alternativa más ligera a los behaviors completos |
| Tipo Result | `Result<T>` / `Result` — éxito/fallo tipado sin excepciones |
| Compensación | `ICompensable` / `Compensable` — rollback al estilo Saga en caso de fallo |

## Filosofía

Vali-Mediator está diseñado en torno a tres principios:

**1. Separación de concerns**

La lógica de negocio vive en los handlers. Los controllers, endpoints y servicios de aplicación solo conocen el mediator — no dependen directamente de repositorios, servicios de dominio u objetos de dominio.

**2. Manejo explícito de errores**

En lugar de lanzar excepciones para fallos de negocio esperados (como "producto no encontrado" o "email ya en uso"), los handlers devuelven `Result<T>`. Esto hace que el camino de fallo sea explícito y con tipo seguro.

**3. Pipeline composable**

Los concerns transversales — logging, validación, autorización, caché — se implementan como pipeline behaviors y se apilan alrededor de cada handler de peticiones. Agregar un nuevo concern no requiere modificar los handlers existentes.

## Visión General de la Arquitectura

```
Cliente (Controller / Endpoint)
    │
    ▼
IValiMediator
    │
    ├── Send<TResponse>(IRequest<TResponse>)
    │       │
    │       ├── [Behavior 1 (más externo)]
    │       ├── [Behavior 2]
    │       ├── [PreProcessors]
    │       ├── IRequestHandler<TRequest, TResponse>
    │       └── [PostProcessors]
    │
    ├── Publish<TNotification>(notification, strategy)
    │       │
    │       ├── [Dispatch Behaviors]
    │       └── INotificationHandler<TNotification> × N (ordenado por Priority)
    │
    ├── Send(IFireAndForget)
    │       │
    │       ├── [Dispatch Behaviors]
    │       └── IFireAndForgetHandler<TFireAndForget>
    │
    └── CreateStream<TResponse>(IStreamRequest<TResponse>)
            │
            └── IStreamRequestHandler<TRequest, TResponse>
                (sin behaviors — llamada directa)
```

## Comparación con MediatR

| Característica | Vali-Mediator | MediatR |
|---|---|---|
| Dependencias externas (core) | Solo DI Abstractions | Ninguna |
| Tipo Result integrado | Sí — `Result<T>`, `Result` | No (requiere terceros) |
| Streaming | `IStreamRequest<T>` | `IStreamRequest<T>` |
| Fire and forget | `IFireAndForget` (con pipeline) | `IRequest` con `Unit` |
| Compensación (Saga) | `ICompensable` integrado | No integrado |
| Prioridad en notificaciones | `INotificationHandler.Priority` | No integrado |
| Estrategias de publish | Sequential, Parallel, ResilientParallel | Sequential, Parallel |
| Pre/Post processors | Auto-descubrimiento desde assembly | Registro manual |
| Frameworks objetivo | .NET 7/8/9 | .NET Standard 2.0+ |

## ¿Cuándo Usar Vali-Mediator?

Vali-Mediator es una buena opción cuando:

- Estás construyendo una aplicación con **arquitectura limpia** o **vertical slices**
- Necesitas **CQRS** sin una infraestructura completa de event sourcing
- Prefieres **tipos Result** en lugar de flujo de errores basado en excepciones
- Necesitas **notificaciones pub/sub** dentro del mismo proceso
- Quieres **streaming** (reportes, datasets grandes) a través de la misma abstracción
- Ya usas el **ecosistema Vali** (Vali-Validation)

## Historial de Versiones

- **v1.x** — Peticiones, notificaciones, fire-and-forget, streaming, pipeline behaviors, processors, compensación
- **v2.0** — Métodos funcionales de `Result<T>` (Map, Bind, Tap, OnFailure), `Result` (no genérico), `ValidationErrors`, `SendOrDefault`, `ResilientParallel`, `RegisterServicesFromAssemblyContaining<T>`, `AddRequestBehavior<T>` / `AddDispatchBehavior<T>`

## Siguientes Pasos

- **[Instalación](02-instalacion.md)** — Agregar el paquete y configurar DI
- **[Inicio Rápido](03-inicio-rapido.md)** — Primera petición en 5 minutos
- **[Result](10-resultado.md)** — Conocer el tipo result integrado
