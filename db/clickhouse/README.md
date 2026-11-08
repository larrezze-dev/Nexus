# Migraciones de ClickHouse (`nexus_analytics`)

Esquema del lado analítico (`conventions-events.md §8`). No usan Flyway: las aplica `nexus-projections` al arrancar.

## Nombre

`V<yyyyMMddHHmm>__<descripcion>.sql`, p. ej. `V202611082100__base.sql`. El orden de aplicación es el orden lexicográfico de la versión.

## Cómo se aplican

1. `nexus-projections` se conecta a la base `default` (la de `nexus_analytics` puede no existir aún).
2. Si `nexus_analytics._schema_migrations` no existe, el historial se considera vacío. La primera migración (`V202611082100__base.sql`) crea la base y esa tabla de control, y es la única fuente de su DDL.
3. Para saber **qué versiones están aplicadas**, el historial se lee con `FINAL` (`SELECT version FROM nexus_analytics._schema_migrations FINAL`): la tabla es `ReplacingMergeTree(applied_at) ORDER BY version` y puede tener varias filas sin fusionar para una misma versión.
4. Ejecuta cada fichero pendiente sentencia a sentencia: separa por `;` y **descarta los fragmentos vacíos o que solo contienen comentarios** (ClickHouse responde 400 "Empty query" si se le envían). Al terminar, inserta una fila en `_schema_migrations` con `version`, `description`, `checksum` (SHA-256 del fichero) y `applied_at`.
5. La comprobación de checksum lee **sin `FINAL`** (`SELECT DISTINCT checksum … WHERE version = ?`), para ver todas las filas de la versión: si alguna no coincide con el SHA-256 del fichero, no arranca. Con `FINAL` solo se vería una fila y una discrepancia podría pasar desapercibida.

### Arranques concurrentes (obligación del track S)

Dos réplicas de `nexus-projections`, o el servicio y un `nexus-projections rebuild`, pueden aplicar la misma versión pendiente a la vez. Debe tolerarse:

- Toda sentencia de una migración es reejecutable, así que ambas ejecuciones terminan bien.
- Ambas registran la versión: puede haber **varias filas iguales por versión**. La comprobación de checksum las acepta. Una sola fila con checksum distinto para esa versión = no arrancar.
- El historial no se carga en una estructura que falle con claves repetidas (p. ej. un diccionario sin deduplicar).
- Un bloqueo de aplicación entre instancias es opcional, no necesario para la corrección.

## Reglas al escribir una migración

- **Una migración mergeada nunca se edita.** Cualquier cambio va en un fichero nuevo.
- El DDL de ClickHouse no es transaccional: si un fichero falla a medias, su versión no se registra y se vuelve a ejecutar entero en el siguiente arranque (o en paralelo por otra instancia). Toda sentencia debe ser reejecutable (`IF NOT EXISTS`, `IF EXISTS`, `ADD COLUMN IF NOT EXISTS`…).
- **Nunca** `INSERT … SELECT` ni otra carga de datos en una migración: reejecutarla duplicaría datos. Los datos llegan por las proyecciones (o por `rebuild`).
- Sin `;` fuera de los terminadores de sentencia (ni en comentarios ni en literales): el aplicador separa por `;`.
- Nombres siempre cualificados: `nexus_analytics.<tabla>`.
- Toda tabla de proyección lleva `legal_entity_id`, `_event_id`, `_aggregate_version`, `_ingested_at` y motor `ReplacingMergeTree(_aggregate_version)` con `ORDER BY` que empieza por `legal_entity_id`. Excepciones documentadas en la cabecera de su fichero: `event_log` (acceso por agregado) y `_schema_migrations` (metadatos de plataforma, no es una proyección). Las consultas usan `FINAL` o `argMax`.

## Probar en local contra un ClickHouse desechable

No uses el contenedor `nexus-clickhouse` del compose para experimentar.

```bash
docker run -d --name ch-scratch -p 18123:8123 \
  -e CLICKHOUSE_USER=tmp -e CLICKHOUSE_PASSWORD=tmp \
  clickhouse/clickhouse-server:26.8.34.13
docker cp db/clickhouse/V202611082100__base.sql ch-scratch:/tmp/m.sql
docker exec ch-scratch clickhouse-client --user tmp --password tmp --multiquery --queries-file /tmp/m.sql
docker exec ch-scratch clickhouse-client --user tmp --password tmp -q "SHOW TABLES FROM nexus_analytics"
docker rm -f ch-scratch
```
