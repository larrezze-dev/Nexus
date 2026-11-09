# ADR-0019 · Versión de agregado persistida (`nexus-aggregate-version`)

- **Estado:** Propuesto
- **Fecha:** 2026-11-09
- **Decide:** Luciano Arrezze
- **Afecta a:** `conventions-events.md §2, §7, §8`, `conventions-database.md` (columnas de las tablas de agregado), `BuildingBlocks.Domain` (`AggregateRoot`), `BuildingBlocks.Persistence` (`DbContextBase`, `UnitOfWork`, `OutboxWriter`), todas las tablas de agregado de `org` y `access` (pasos 0.4 y 0.6), proyecciones `dim_*` (0.8) y réplicas `*_ref` (`VersionedReplica`, 0.7)

## Contexto

`conventions-events.md §2` define la cabecera `nexus-aggregate-version` como «entero, crece por agregado». Los consumidores que necesitan orden comparan esa versión y descartan las anteriores (§7). Lo mismo hacen `ReplacingMergeTree(_aggregate_version)` en las proyecciones `dim_*` (§8) y el `UPSERT … WHERE EXCLUDED.aggregate_version > r.aggregate_version` de `VersionedReplica` (0.7).

La verificación en vivo del paso 0.8 (track S, 2026-11-09) mostró que **todos los eventos salen con versión 1**. Al crear un almacén y editarlo dos veces se obtuvieron tres eventos, los tres con `aggregate_version = 1`. La causa:

- `AggregateRoot.AggregateVersion` solo vive en memoria: `RaiseEvent` la incrementa en cada evento.
- `DbContextBase` (`AggregateRootMembersConvention`) la excluye del modelo de EF, así que no se guarda ni se carga.
- Al cargar el agregado vale 0, y el primer evento de cada comando sale con 1.

Consecuencias:

1. `event_log` (auditoría) ordena bien solo gracias al desempate por `event_id`, que es UUIDv7 y crece en el tiempo.
2. `dim_legal_entity`/`dim_warehouse` no distinguen versiones. Si un evento antiguo se reprocesa después de uno nuevo (redelivery tras un rebalance, at-least-once), puede quedar como estado vigente el antiguo.
3. `VersionedReplica` descartaría todos los updates posteriores al `created`. Todavía no la usa ningún consumidor.

## Decisión (propuesta)

1. Cada tabla de agregado lleva una columna `aggregate_version bigint NOT NULL DEFAULT 0`. Es el número de eventos que ha emitido el agregado.
2. `AggregateRoot.AggregateVersion` se mapea a esa columna (deja de ignorarse en EF) y se carga con el agregado. `RaiseEvent` la sigue incrementando; el `UnitOfWork` la guarda en la misma transacción que el `outbox.message`, y la cabecera sale de ella como hoy.
3. La columna es **token de concurrencia** de EF: `UPDATE … WHERE aggregate_version = <cargada>`. Así, dos comandos concurrentes sobre el mismo agregado no pueden emitir la misma versión: el segundo falla con conflicto (409) y nunca publica un duplicado. El `ETag`/`If-Match` actual (`xmin`) no cambia.
4. **Backfill** (track C, una migración versionada por esquema afectado): las filas existentes se inicializan a 1. Todos los eventos ya publicados llevan 1, así que el siguiente sale con 2 y desde ahí la serie es monótona. No se reescribe ningún evento ya publicado.
5. **Tablas excluidas:** las append-only y las versionadas VR1 no son agregados mutables, cada fila es su propia versión. La lista exacta de tablas la fija el track C de este cambio.
6. Tras desplegarlo, en los entornos con datos se ejecuta `nexus-projections rebuild dim_legal_entity` y `rebuild dim_warehouse`. `event_log` no lo necesita: sus filas antiguas mantienen su versión 1 y el desempate por `event_id`.

## Por qué

- Es lo que ya dice `conventions-events §2`: el fallo está en la implementación, no en el contrato de cabeceras.
- Persistir el contador es la única fuente fiable de una versión monótona por agregado. `xmin` no sirve: es un id de transacción, no un contador por agregado, y da la vuelta. Contar filas de `outbox.message` tampoco: se purgan a los 7 días (0.7).
- Usar la columna como token de concurrencia convierte «la versión crece» en una garantía, y no en una convención que dos comandos concurrentes podrían romper.

## Alternativas descartadas

| Alternativa | Por qué no |
|---|---|
| Derivar la versión de `max(outbox.message.aggregate_version)` del agregado | `outbox.message` se purga (retención de 7 días, 0.7); el máximo desaparece. |
| Usar `xmin` o `txid` como versión | No es un contador por agregado; da la vuelta (wraparound); no sirve para comparar `>` entre eventos de distintos agregados ni tras un restore. |
| Arreglarlo en los consumidores (ordenar por `occurred_at` o `event_id`) | Contradice `conventions-events §2/§7/§8`. `occurred_at` puede empatar con resolución de microsegundos. Cada consumidor tendría que reimplementarlo. |
| No hacer nada hasta Fase 1 | `dim_*` (0.8) ya depende de ello, y el primer consumidor `*_ref` también dependerá. Gate D de 0.8 quedaría con un fallo conocido. |

## Implementación (si se acepta)

- **Track C:** migraciones `V…__<esquema>__aggregate_version.sql` (columna, backfill a 1, `NOT NULL`) para cada tabla de agregado de `org` y `access`, más el ajuste en `conventions-database.md`.
- **Nivel 3 (nx-architect):** `DbContextBase` mapea la columna como token de concurrencia. `UnitOfWork`/`OutboxWriter` mantienen el cálculo `AggregateVersion - n + i + 1`. Conflicto de concurrencia → 409 por el camino existente.
- **Tests:**
  - tres comandos seguidos sobre el mismo agregado emiten las versiones 1, 2 y 3 (y 2, 3, 4 tras el backfill);
  - dos comandos concurrentes: uno gana y el otro recibe 409, sin versión duplicada en el outbox;
  - `dim_warehouse` conserva el estado más nuevo cuando se reprocesa un evento viejo;
  - `VersionedReplica` aplica los updates.
- **Debe estar antes de:** Gate D del paso 0.8 y el primer consumidor `*_ref`.
