# ADR-0013 · Tipo de columna para `outbox.message.payload`

- **Estado:** Aceptado
- **Fecha:** 2026-10-23 (decidido 2026-10-24)
- **Decide:** Luciano Arrezze
- **Afecta a:** `conventions-events.md §3, §6`, `db/migrations/V202610231001__infra__outbox_inbox.sql` (0.3, ya en `main`), todo paso futuro cuyos eventos lleven un campo `decimal` (dinero, cantidades, porcentajes, tipo de cambio)

## Contexto

`outbox.message.payload` es `jsonb` (`conventions-events.md §3`). El payload se codifica como JSON de Avro (EV-R1, ADR-0008) y el tipo `decimal` de Avro se modela como `bytes` con `logicalType: decimal` (`conventions-events.md §6`). El codificador JSON de Apache Avro representa los bytes de ese campo como una cadena con escapes `\uXXXX` por cada byte — y PostgreSQL **rechaza** `jsonb` que contenga un escape `\u0000` (error `22P05`, "unsupported Unicode escape sequence").

Esto no es un caso límite raro: cualquier valor `decimal` cuya representación en complemento a dos de dos bytes contenga un byte `0x00` lo dispara — por ejemplo, **16.0000** y **256.0000** fallan tal cual. Descubierto en el paso 0.3 (track B, `OutboxWriter`/`AvroPayloadSerializer`) con un test que reproduce el fallo de forma determinista contra un Postgres real; la transacción se aborta limpiamente (no hay corrupción), pero **ningún evento con un campo `decimal` puede escribirse nunca** tal como está el contrato hoy. Ningún paso de 0.3 emite un evento con `decimal`, así que no bloquea su Aceptación, pero bloquearía el primer evento de dinero/cantidad de 0.4 en adelante (prácticamente todos los contextos de negocio).

## Decisión

**(A)** — `outbox.message.payload` pasa de `jsonb` a `text`. No cambia el contrato de eventos ya fijado por ADR-0008/EV-R1 (sigue siendo JSON de Avro); es una migración aditiva de una sola columna sin tocar la versionada ya mergeada (`db/migrations/V202610231001...` no se edita — ADR-0001 — se añade una nueva `V<ts>__infra__outbox_payload_text.sql` con `ALTER TABLE outbox.message ALTER COLUMN payload TYPE text`). El binding de Npgsql del `OutboxWriter` (`NpgsqlDbType.Jsonb` → `NpgsqlDbType.Text` para el parámetro `payload`) cambia junto con la migración.

## Alternativas descartadas

| Alternativa | Por qué no (o por qué sí, a decidir) |
|---|---|
| **(A) `payload` pasa de `jsonb` a `text`** | Conserva EV-R1 (JSON de Avro) y el tipo `decimal` de Avro tal cual. Pierde la posibilidad de indexar/consultar el payload con operadores `jsonb` nativos de Postgres — pero hoy nada lo hace (el relay solo lee la fila entera y la reenvía; no hay consultas por campos del payload). Coste: una migración nueva, aditiva. |
| **(B) Los `decimal` viajan como `string` en Avro, no como `bytes`/`logicalType: decimal`** | Evita el problema de raíz (un `string` nunca produce `\u0000` en JSON), pero reabre `conventions-events.md §6` (tabla de tipos Avro) y cambia cómo todo consumidor futuro (incluido ClickHouse, `nexus-projections`) interpreta los importes — pierde la semántica de precisión/escala explícita que da el `logicalType: decimal`. |
| **(C) El payload se guarda en binario Avro (no JSON) dentro de una columna `bytea`** | Más eficiente, pero contradice EV-R1 explícitamente ("el payload se guarda en JSON... la transacción de negocio no depende de que el Schema Registry esté disponible" — la codificación binaria de Avro normalmente sí depende de un Schema Registry o de empaquetar el esquema junto al dato). Cambio de mayor alcance. |

## Consecuencias

- Con (A): ningún cambio en cómo los contextos de dominio describen sus eventos (`.avsc`, `conventions-events.md §6` intactos). El relay (0.7) sigue leyendo `payload` tal cual y lo pasa a Confluent Schema Registry para la conversión a binario — ya esperaba una cadena de texto con JSON de Avro, cambia solo el tipo de columna PostgreSQL que la contiene, no su contenido.
- Si se posterga la decisión, el primer paso que intente emitir un evento con un campo `decimal` (dinero, cantidad, porcentaje o tipo de cambio) lo descubrirá en caliente, con una transacción abortada y sin pista previa en `docs/` — mejor cerrarlo antes de 0.4.

## Cómo se verifica

Test de integración (ya escrito en `Nexus.BuildingBlocks.Persistence.Tests`, hoy marcado como "limitación conocida"): un evento de prueba con un campo `decimal` cuyo valor produce un byte `0x00` en su codificación Avro (p. ej. `16.0000`) se inserta en `outbox.message` sin error, y se lee de vuelta con el mismo valor exacto.
