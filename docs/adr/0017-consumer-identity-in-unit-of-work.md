# ADR-0017 · Identidad del consumidor dentro del `UnitOfWork`

- **Estado:** Aceptado (Luciano, 2026-11-07: opción A)
- **Fecha:** 2026-11-07
- **Decide:** Luciano Arrezze
- **Afecta a:** `conventions-events.md §7`, `conventions-backend.md §4`, `Nexus.BuildingBlocks.Messaging` (`Inbox/`), `Nexus.BuildingBlocks.Persistence` (`UnitOfWork`), todo servicio que tenga a la vez API HTTP y consumidores (paso 0.7 track S en adelante)

## Contexto

`conventions-events.md §7` exige que el consumidor abra su transacción con `app.current_user_id` = `actor_user_id` del evento (o SYSTEM). `UnitOfWork` obtiene el usuario de `ICurrentUser`, y los servicios de negocio registran `HttpCurrentUser` (basado en `HttpContext`). Un consumidor no tiene `HttpContext`, así que `BeginAsync` fallaría. Hace falta un mecanismo que dé al `UnitOfWork` la identidad del evento sin romper el camino HTTP. Esta decisión no está escrita en `docs/`.

## Decisión propuesta

**(A)** `AddNexusInbox()` sustituye el registro de `ICurrentUser` por un decorador (`ConsumerAwareCurrentUser`): devuelve la identidad del evento solo si `InboxProcessor` fijó `ConsumerIdentity` en ese scope DI; en cualquier otro caso delega en la implementación original (HTTP). El consumidor crea un scope DI por mensaje. En scopes de consumidor solo `UserId` está informado (`PartyId`/`BusinessUnitId`/`Oid` = null). `AddNexusInbox()` debe llamarse después de `AddNexusApplication`, `AddNexusPersistence` y `AddNexusAuthentication`.

## Alternativas

| Alternativa | Valoración |
|---|---|
| **(A) Decorador de `ICurrentUser`** (propuesta) | No toca `Persistence`. Riesgo: depende del orden de registro en DI; los scopes HTTP no están protegidos contra un orden erróneo. El procesador falla en tiempo de ejecución si la identidad del consumidor no se refleja. |
| **(B) Override de identidad dentro de `UnitOfWork`** (p. ej. `BeginAsync(identity)`) | Sin magia de DI y explícito, pero modifica `BuildingBlocks.Persistence` (ya `Aprobado` en 0.3) y el contrato de `IRequestTransaction`. |
| **(C) Un `ICurrentUser` propio para hosts solo-consumidor** | Simple, pero no sirve a servicios con HTTP y consumidores a la vez (la mayoría). |

## Consecuencias

- Con (A): sin cambios en `Persistence`; el orden de `Add*` pasa a ser un invariante a documentar y revisar (`security-review` sobre el decorador antes del merge).
- Efecto secundario aceptado: un efecto que despache un handler de comandos debe convertir un `Result` fallido en excepción; si no, el procesador confirma lo escrito antes del fallo.
- Mitigaciones implementadas del riesgo de (A) (paso 0.7, track S):
  - `IInboxProcessor`/`InboxProcessor` son `internal` y no se registran en DI: ningún endpoint ni handler puede resolverlos para suplantar a un usuario; solo `NexusConsumer` los instancia, uno por intento, en su propio scope (`ActivatorUtilities.CreateInstance`).
  - El procesador se niega a ejecutarse si el scope ya tiene un `ICurrentUser` autenticado o si hay un `HttpContext` (petición HTTP), antes de fijar la identidad del consumidor.
  - Si tras fijarla el `ICurrentUser` del scope no la refleja (p. ej. un `ICurrentUser` registrado después de `AddNexusInbox`), se niega a abrir la transacción.
  - La identidad del consumidor se borra en un `finally` al terminar `ProcessOnceAsync`, haya éxito o error.
- Con `OnBehalfOfActor` la identidad sale de la cabecera `nexus-actor-user-id` del evento, así que quien pueda producir en un topic puede actuar como cualquier usuario: las ACL de producción de Kafka son una frontera de seguridad (se aplicarán en el paso 0.11).

## Cómo se verifica

Tests `EV-INBOX` del track S: `app.current_user_id` = actor (o SYSTEM) en la transacción del consumidor; un scope HTTP del mismo host conserva el usuario HTTP.
