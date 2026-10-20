# Eventos: convención de nombres

Resumen operativo de `docs/00-foundation/conventions-events.md §5–§6`. Ante
cualquier duda, manda ese documento, no esto.

## Topics

- Nombre: `nexus.{contexto}.{aggregate}.{evento}`, en kebab-case.
  Ejemplos: `nexus.stock.movement.posted`, `nexus.commercial.contract.signed`.
- Contextos válidos: `platform`, `org`, `catalog`, `project`, `stock`,
  `logistics`, `solver`, `commercial`, `billing`, `hr`, `controlling`,
  `warehouse`, `commissioning`, `field`. Los contextos `access`, `workflow`,
  `docs` y `gdpr` publican como `platform`.
- Verbo en participio pasado: `created`, `updated`, `deactivated`, `posted`,
  `confirmed`, `versioned`…
- Eventos de versionado (A10): `nexus.{contexto}.{aggregate}.versioned`
  (p. ej. `nexus.catalog.price-rule.versioned`).
- Un cambio de schema incompatible crea `v<n+1>` en un **topic nuevo**
  (`...posted.v2`); el topic antiguo se mantiene hasta migrar los
  consumidores.
- DLQ: `{topic}.DLQ`.
- Cada topic que se active se da de alta en `contracts/events/topics.yaml`
  (nombre, contexto, agregado, evento, versión, compatibilidad). Un topic que
  no está en ese fichero no existe (`auto.create.topics.enable=false`).

## Ficheros `.avsc`

- Ruta: `contracts/events/<contexto>/<aggregate>.<evento>.v<n>.avsc`
  (p. ej. `contracts/events/stock/movement.posted.v1.avsc`).
- `namespace`: `ch.nussli.nexus.<contexto>`.
- `name` del record: `<Aggregate><Evento>` en PascalCase
  (p. ej. `AssetTypeCreated`).
- Campos del payload en `camelCase`.
- Subject en el Schema Registry: `<topic>-value` (TopicNameStrategy).
  Compatibilidad **BACKWARD** obligatoria (D8): un campo nuevo es opcional
  con `default`; nunca se elimina ni se renombra un campo existente.
- El payload va en el valor del mensaje; el envoltorio común (`event_id`,
  `event_type`, `aggregate_id`, `aggregate_version`, `occurred_at`,
  `actor_user_id`, `schema_version`, `correlation_id`, `causation_id`,
  `legal_entity_id`) viaja en cabeceras Kafka, no en el `.avsc`
  (conventions-events.md §2).

Tipos Avro de uso frecuente (conventions-events.md §6): UUID como
`{"type":"string","logicalType":"uuid"}`, instantes como
`{"type":"long","logicalType":"timestamp-micros"}`, importes y cantidades
como `{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}`
(importes) o `precision:18` (cantidades), estados como `string` simple
(nunca `enum` de Avro), campos opcionales como `["null", <tipo>]` con
`"default": null`, y texto traducible como `{"type":"map","values":"string"}`.

Este directorio está vacío hasta que un paso de un contexto concreto
defina sus primeros eventos.
