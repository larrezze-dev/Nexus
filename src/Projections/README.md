# nexus-projections

Worker Kafka → ClickHouse (`conventions-events.md §8`, paso 0.8). Un consumidor por proyección (grupo `projections.<proyeccion>`), inserción por lotes en `nexus_analytics`, idempotencia por `ReplacingMergeTree`.

## Ejecutar

```bash
nexus-projections [run] [--Clave=Valor ...]          # worker: migraciones de ClickHouse, después los consumidores; sirve /health
nexus-projections rebuild <proyeccion> [--Clave=Valor ...]
nexus-projections help | --help | -h                  # uso; código 0, nunca arranca consumidores
```

Códigos de salida: `0` correcto · `1` arranque rechazado (migraciones), `rebuild` rechazado o fallido, o un bucle de consumo murió · `2` comando o proyección desconocidos.

Las proyecciones se registran en un único sitio: `ProjectionsCli.RegisterProjections` → `AddNexusProjectionDefinitions()` (`Nexus.Projections/Projections/`). El catálogo se resuelve al arrancar (`run` y `rebuild`): un `topics.yaml` mal formado, nombres repetidos o identificadores inválidos paran el proceso antes de consumir.

## Arranque

1. Aplica `db/clickhouse/V*.sql` según `db/clickhouse/README.md` (base `default`, historial con `FINAL`, checksum sin `FINAL`, arranques concurrentes tolerados). Checksum distinto o fichero ausente de una versión aplicada → no arranca.
2. Después arranca un consumidor por proyección. Un error fatal de Kafka en cualquiera detiene el host (`StopHost`) con código ≠ 0.

## `rebuild <proyeccion>`

1. Migraciones.
2. Si el grupo `projections.<proyeccion>` tiene miembros activos → rechaza: *"stop nexus-projections before rebuilding <proyeccion>"*.
3. Borra los offsets del grupo (`DeleteGroups`; Kafka lo rechaza si alguien se unió entretanto, y entonces la tabla sigue intacta), y después `TRUNCATE TABLE`.
4. Toma los high watermarks de todos los topics que encajan con la suscripción y los relee desde el principio con el mismo pipeline que el worker. Cada partición termina al alcanzar su watermark, con un fin de partición en o tras él, o si su topic se borró.
5. Cada lote confirma sus offsets; el último deja al worker justo donde terminó la reconstrucción.

Los registros venenosos **no** se vuelven a publicar en la DLQ (decisión D2: el worker ya lo hizo): se cuentan, se registran por topic/partición/offset sin payload y el resumen los incluye; la salida sigue siendo 0. Los errores transitorios de admin de Kafka (coordinador cargando o moviéndose, timeouts) se reintentan unas pocas veces.

Ctrl+C: el lote en curso no se inserta ni se confirma; los lotes ya volcados quedan insertados y confirmados (tabla y offsets coinciden). Vuelve a lanzar `rebuild`.

## Clases de fallo

| Fallo | Ejemplos | Qué hace | Métrica `nexus_projection_errors_total{kind}` |
|---|---|---|---|
| Del registro | envoltorio inválido, bytes que no son Avro, schema id desconocido (404), `Map` lanza | 4 intentos (200 ms / 1 s / 5 s) → `{topic}.DLQ` con `nexus-dlq-*` (worker) o contado y saltado (`rebuild`) | `record`; `nexus_projection_dlq_total` |
| Schema Registry no disponible (D3) | inalcanzable, timeout, 5xx, 408, 429, credenciales rechazadas 401/403/407 | pausa, `/health` Unhealthy (`DependencyUnavailable`), backoff 1 s → 30 s, nunca DLQ ni commit | `schema_registry` |
| ClickHouse no disponible | red, timeout, 5xx, tabla o base inexistente | igual: lote retenido, pausa, Unhealthy, reintento | `insert_unavailable` |
| ClickHouse **rechaza** el lote (D1) | tipo, parseo o valor (códigos 6, 16, 26, 27, 32, 33, 38, 41, 43, 53, 69, 70, 72, 117, 349, 376) o error de serialización en el cliente | **bloquea** la proyección igual que una caída (sin commit, sin DLQ, sin pérdida); log ERROR *"projection <name> blocked: ClickHouse rejected the batch, fix the projection and rebuild"* sin valores de fila | `insert_rejected` (alertar ante cualquier incremento) |
| DLQ no disponible | broker caído, topic `.DLQ` inexistente | pausa, Unhealthy (`DlqUnavailable`), reintenta la publicación; nunca salta el registro | `dlq` |
| Sondeo de Kafka | topic desconocido, broker caído | Degraded, backoff 1 s → 5 s | `poll` |
| Commit tras insertar | rebalanceo | se registra; los registros se releen y sus filas se colapsan | `commit` |

Garantías: un offset solo se confirma tras insertar sus filas; en un rebalanceo el lote de las particiones revocadas se descarta sin confirmar y lo relee su siguiente dueño (at-least-once; `ReplacingMergeTree` colapsa); una cancelación nunca confirma.

## Health

`GET /health` (sin autenticación). Estados por grupo (`<grupo>.state`, `<grupo>.lag`): Unhealthy si un bucle murió o está pausado por una dependencia (`DependencyUnavailable`, `DlqUnavailable`); Degraded por errores de sondeo, lag mayor que `Nexus:Consumers:Health:LagDegradedThreshold` o lag desconocido. El lag suma todos los topics de la suscripción (regex incluida, sin `.DLQ`).

Puerto: si nada lo configura, `http://localhost:5301`. `ASPNETCORE_URLS`, `--urls`, `Urls` o `ASPNETCORE_HTTP_PORTS` siempre ganan (compose: `ASPNETCORE_URLS=http://+:5301`). ⚑ El puerto 5301 aún no figura en `architecture.md`.

## Métricas (meter `Nexus.Projections`, OTLP en `Nexus:Telemetry:OtlpEndpoint`)

`nexus_projection_lag_seconds{projection}` (histograma por fila: fin del insert − `occurred_at`; LOG-R45 p95 < 5 s) · `nexus_projection_rows_total` · `nexus_projection_dlq_total` · `nexus_projection_errors_total{projection,kind}` · `nexus_projection_batch_size`.

## Configuración

| Clave | Por defecto |
|---|---|
| `Nexus:Kafka:BootstrapServers` / `SchemaRegistryUrl` / `DlqMessageTimeout` / `MaxPollInterval` | `localhost:9092` / `http://localhost:8081` / 30 s / 5 min |
| `Nexus:ClickHouse:ConnectionString` | — (obligatoria; contraseña por entorno o user-secrets, nunca en el repo). Se usa siempre la base `default` |
| `Nexus:Projections:MaxBatchRows` / `FlushInterval` | 1000 / 1 s |
| `Nexus:Projections:MigrationsDirectory` | `<binarios>/db/clickhouse` |
| `Nexus:Projections:TopicMetadataRefreshInterval` | 30 s (cuánto tarda una suscripción por regex en ver un topic nuevo) |
| `Nexus:Projections:TransientInitialBackoff` / `TransientMaxBackoff` | 1 s / 30 s |
| `Nexus:Projections:SchemaRegistryRequestTimeout` / `AdminRequestTimeout` | 10 s / 15 s |
| `Nexus:Consumers:Health:*` | ver `ConsumerHealthOptions` |

## Tests

```bash
dotnet test --project src/Projections/Nexus.Projections.Tests
```

Testcontainers (Kafka + Schema Registry + ClickHouse); nunca usan los contenedores `nexus-*` del compose.
