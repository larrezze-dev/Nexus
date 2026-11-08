# Migraciones de ClickHouse (`nexus_analytics`)

Esquema del lado analítico (`conventions-events.md §8`). No usan Flyway: las aplica `nexus-projections` al arrancar.

## Nombre

`V<yyyyMMddHHmm>__<descripcion>.sql`, p. ej. `V202611090900__base.sql`. El orden de aplicación es el orden lexicográfico de la versión.

## Cómo se aplican

1. `nexus-projections` se conecta a la base `default` (la de `nexus_analytics` puede no existir aún).
2. Si `nexus_analytics._schema_migrations` no existe, el historial se considera vacío. La primera migración (`V202611090900__base.sql`) crea la base y esa tabla de control, y es la única fuente de su DDL.
3. Ejecuta cada fichero pendiente sentencia a sentencia (separador `;`) y, al terminar, inserta una fila en `_schema_migrations` con `version`, `description`, `checksum` (SHA-256 del fichero) y `applied_at`.
4. Si el checksum de una versión ya aplicada no coincide con el del fichero, no arranca.

## Reglas al escribir una migración

- **Una migración mergeada nunca se edita.** Cualquier cambio va en un fichero nuevo.
- El DDL de ClickHouse no es transaccional: si un fichero falla a medias, su versión no se registra y se vuelve a ejecutar entero en el siguiente arranque. Toda sentencia debe ser reejecutable (`IF NOT EXISTS`, `IF EXISTS`, `ADD COLUMN IF NOT EXISTS`…).
- Sin `;` fuera de los terminadores de sentencia (ni en comentarios ni en literales): el aplicador separa por `;`.
- Nombres siempre cualificados: `nexus_analytics.<tabla>`.
- Toda tabla lleva `legal_entity_id`, `_event_id`, `_aggregate_version`, `_ingested_at` y motor `ReplacingMergeTree(_aggregate_version)` con `ORDER BY` que empieza por `legal_entity_id`, salvo excepción documentada en la cabecera del fichero (`event_log`). Las consultas usan `FINAL` o `argMax`.

## Probar en local contra un ClickHouse desechable

No uses el contenedor `nexus-clickhouse` del compose para experimentar.

```bash
docker run -d --name ch-scratch -p 18123:8123 \
  -e CLICKHOUSE_USER=tmp -e CLICKHOUSE_PASSWORD=tmp \
  clickhouse/clickhouse-server:26.8.34.13
docker cp db/clickhouse/V202611090900__base.sql ch-scratch:/tmp/m.sql
docker exec ch-scratch clickhouse-client --user tmp --password tmp --multiquery --queries-file /tmp/m.sql
docker exec ch-scratch clickhouse-client --user tmp --password tmp -q "SHOW TABLES FROM nexus_analytics"
docker rm -f ch-scratch
```
