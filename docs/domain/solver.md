# Dominio · Isla Solver (Spring Boot + Timefold)

- **Origen:** §1 (Capa MD 3 = restricciones del Solver), Parte 3 §17, D2, D4, P6 (`svc_solver`), ADR-0012 (interfaz y modelo)
- **Esquema:** ninguno propio. Lee `org`, `uom`, `catalog`, `project`, `stock`, `logistics` con `svc_solver` (`BYPASSRLS`, solo lectura) y ClickHouse `nexus_analytics`
- **Servicio:** `solver` (`solver/`, Kotlin/JVM) · **Contrato:** `contracts/openapi/solver.yaml`, `contracts/events/solver/run.completed.v1.avsc`
- **Pasos:** 4.4 (isla, entrada, modelo, API, evento), 4.5 (consumo en operations → `Transfer` `DRAFT`, en `logistics.md`)

## §1 Principio

El Solver **no se diseña hasta tener completas las tres capas de master data** (S5). Consume configuración: catálogo (Capa 2), reglas de alquiler y transferencia y matriz de transporte (Capa 3) y la disponibilidad derivada (A2).

Es la única excepción JVM de servidor (D2) y es **sustituible**: solo depende de un contrato REST y un evento. No escribe en PostgreSQL (P6). Ninguna propuesta se ejecuta sola: `nexus-operations` las convierte en `Transfer` `DRAFT` para revisión humana (LOG-R60, LOG-R61).

## §2 Problema v1 `[+]` (ADR-0012)

> Dadas las reservas de material (`logistics.reservation`) de un horizonte, decidir **desde qué ubicación de stock** se sirve cada una, minimizando el coste logístico y los déficits, sin superar la disponibilidad de ninguna ubicación en ningún momento.

Fuera de alcance v1: rutas multiparada, asignación de vehículos concretos, planificación de personal, optimización de la vuelta. Cada extensión será un ADR propio.

## §3 Modelo de planificación (Timefold)

### Hechos del problema (inmutables durante la corrida)

| Clase Kotlin | Origen | Contenido |
|---|---|---|
| `SupplyLocation` | `stock.stock_location` tipo `WAREHOUSE` (vía `org.warehouse`) | id, `legalEntityId` (custodio), `locationId` (`org.location`) |
| `AssetTypeFact` | `catalog.asset_type` | id, `trackingMode`, `unitWeightKg`, `unitVolumeM3` |
| `SupplyCurve` | ClickHouse (§4) | por `(supplyLocation, assetType)`: disponibilidad libre por día del horizonte, **excluidas** las reservas que se planifican en esta corrida |
| `Lane` | `project.transport_cost_matrix` vigente | `(fromLocation, toLocation, vehicleClass, fleetType)` → `costPerTrip`, `durationMinutes`, `distanceKm` |
| `TransferRuleFact` | `project.transfer_rule` resuelta (PRJ-R14) | por `(fromLE, toLE)`: `isAllowed`, `minLeadTimeHours`, `handlingCost` |
| `RentalRuleFact` | `project.rental_rule` resuelta (PRJ-R13) | por `(assetType, ownerLE)`: `allowInternalRental` |
| `InternalPrice` | `catalog.asset_type_price_rule` `INTERNAL` vigente | por `(assetType, ownerLE)`: precio por día |
| `VehicleClassFact` | `project.vehicle_class` | la clase por defecto de la corrida (parámetro) |

### Entidad de planificación

```kotlin
@PlanningEntity
class DemandChunk(
    @PlanningId val id: String,                 // "<reservationId>#<n>"
    val reservationId: UUID,
    val assetTypeId: UUID,
    val demandLegalEntityId: UUID,
    val destinationLocationId: UUID,            // org.location del recinto
    val periodStart: Instant, val periodEnd: Instant,
    val qty: BigDecimal,                        // en stock_uom
    val defaultSourceId: UUID?,                 // LOG-R24, para la preferencia blanda
) {
    @PlanningVariable(allowsUnassigned = true)  // null = déficit
    var source: SupplyLocation? = null
}
```

**Troceo `[+]`:** cada reserva se divide en `min(5, ceil(qty))` trozos de cantidad lo más igual posible (los `SERIALIZED` en trozos enteros). Permite servir una reserva desde varios almacenes como máximo en 5 partes. Parámetro `maxSplitsPerReservation` (por defecto 5).

### Restricciones (`ConstraintProvider`)

| Id | Nivel | Restricción |
|---|---|---|
| SLV-H1 | hard | **Capacidad:** para cada `(source, assetType, día)` del horizonte, Σ `qty` de trozos asignados cuyo periodo cubre ese día ≤ `SupplyCurve(source, assetType, día)` |
| SLV-H2 | hard | **Regla de transferencia:** si `source.legalEntityId ≠ demandLegalEntityId` y la regla resuelta no lo permite (`isAllowed = false`, o no hay regla `INTER_LE`, PRJ-R14) |
| SLV-H3 | hard | **Alquiler interno:** si la LE propietaria del stock en `source` ≠ demanda y `allowInternalRental = false` |
| SLV-H4 | hard | **Plazo:** `periodStart − duración del carril − minLeadTimeHours` < instante de la corrida |
| SLV-H5 | hard | **Carril:** no existe `Lane` vigente entre `source.locationId` y `destinationLocationId` (en ningún sentido si no es simétrico) para la clase de vehículo por defecto |
| SLV-M1 | medium | **Déficit:** penaliza la cantidad de trozos sin asignar (× 1000 por unidad) |
| SLV-S1 | soft | **Transporte:** agrupa trozos por `(source, destino, día de salida)`; viajes = `ceil(Σ peso / max_payload_kg)` (y por volumen; se toma el mayor); penaliza viajes × `costPerTrip` |
| SLV-S2 | soft | **Manipulación:** por cada grupo de SLV-S1 con LEs distintas, `handlingCost` de la regla |
| SLV-S3 | soft | **Alquiler interno:** días del periodo × `qty` × precio `INTERNAL` del propietario, si la LE propietaria ≠ demanda |
| SLV-S4 | soft | **Fragmentación:** (número de orígenes distintos por reserva − 1) × 50 |
| SLV-S5 | soft | **Preferencia:** recompensa 10 si `source = defaultSourceId` |

Todos los costes soft se convierten a la moneda de informe del grupo (`org."group".reporting_currency_code`) con el último `org.exchange_rate` y se puntúan en céntimos (`long`). Cada restricción tiene test `ConstraintVerifier` que la cita por id.

### Terminación

`spentLimit` = `timeLimitSeconds` de la petición (por defecto 30, máximo 300); `unimprovedSpentLimit` = 10 s. Algoritmo por defecto de Timefold (construcción + búsqueda local).

## §4 Entrada

**Selección de reservas a planificar** (PostgreSQL, `svc_solver`):

```sql
SELECT r.* FROM logistics.reservation r
JOIN catalog.asset_type t ON t.id = r.asset_type_id
WHERE (r.status = 'FIRM' OR (:includeSoft AND r.status = 'SOFT'))
  AND r.legal_entity_id = ANY(:legalEntityIds)
  AND r.period_start < :horizonTo AND r.period_end > :horizonFrom
  AND r.qty_stock_uom - r.converted_qty > 0
  AND t.kit_mode <> 'VIRTUAL'                 -- el padre de un kit VIRTUAL no se planifica; sus hijas sí (LOG-R25)
  AND (r.source_stock_location_id IS NULL OR :replanAssigned)
  AND (cardinality(:jobIds) = 0 OR r.job_id = ANY(:jobIds));
```

**Curva de oferta** (ClickHouse): para cada almacén y tipo afectados, por día del horizonte:
`OnHand + Inbound(≤ día) − Σ open_qty × weight de reservas activas ese día **que no están en el conjunto planificado**`
usando `stock_delta`, `inbound_expected` y `reservation_state` (`logistics.md §8`), con `FINAL`. Es la misma fórmula que LOG-R40 con la exclusión.

El Solver toma el instante de la corrida (`requestedAt`) una sola vez y lo usa para todas las lecturas.

## §5 Salida: evento `nexus.solver.run.completed`

Avro `ch.nussli.nexus.solver.RunCompleted`:

| Campo | Contenido |
|---|---|
| `runId`, `requestedBy`, `requestedAt`, `finishedAt` | |
| `legalEntityIds`, `horizonFrom`, `horizonTo` | Alcance |
| `status` | `COMPLETED` · `FAILED` · `CANCELLED` |
| `score` | `"0hard/-120medium/-45000soft"` |
| `proposals[]` | `reservationId`, `assetTypeId`, `ownerLegalEntityId`, `qtyStockUom`, `sourceStockLocationId`, `destinationLocationId`, `shipBy`, `estimatedCost`, `currencyCode`, `isIntercompany`, `reservationVersion` (= `xmin` de la fila de reserva leída, para LOG-R62) |
| `deficits[]` | `reservationId`, `assetTypeId`, `projectId`, `periodStart`, `periodEnd`, `shortageQty`, `suggestedAction` (`PURCHASE` si hay proveedor `PURCHASE` preferido con `lead_time_days` que llega a tiempo; si no, `SUBHIRE` si lo hay; si no, `NONE`), `suggestedSupplierPartyId`, `estimatedCost` |
| `error` | Si `FAILED` |

Topic `nexus.solver.run.completed`, clave = `runId`. Las propuestas de un mismo trozo se agregan por `(reservationId, source)`.

## §6 API (`solver.yaml`)

| Método y ruta | Permiso | Notas |
|---|---|---|
| `POST /v1/runs` | `SOLVER:CREATE` en **cada** LE de `legalEntityIds` | Cuerpo: `legalEntityIds[]`, `horizonFrom`, `horizonTo`, `jobIds[]?`, `includeSoft` (false), `replanAssigned` (false), `defaultVehicleClassId`, `timeLimitSeconds` (30). 202 + `runId` |
| `GET /v1/runs/{runId}` | `SOLVER:READ` | Solo mientras la corrida vive en memoria: estado, puntuación actual, segundos transcurridos. Después: 404 (el historial está en `operations`, `GET /v1/solver-runs/{id}`) |
| `POST /v1/runs/{runId}/cancel` | `SOLVER:CREATE` | Termina antes de tiempo; publica `CANCELLED` con la mejor solución hasta ese momento |
| `GET /health` | `PUBLIC` | |

Autorización `[+]`: el Solver valida el JWT igual que los servicios .NET y llama a `access.has_permission('SOLVER:CREATE', le)` abriendo una transacción con `SET LOCAL app.current_user_id`. `svc_solver` tiene `EXECUTE` sobre `access.has_permission` y `access.current_user_id` (se añade en 4.4). Máximo **2 corridas simultáneas** por instancia; la tercera → 429.

## §7 Reglas

| Id | Regla | Origen |
|---|---|---|
| SLV-R1 | El Solver nunca escribe en PostgreSQL. Su único efecto es el evento `run.completed` | P6, ADR-0012 |
| SLV-R2 | Cada corrida registra en el log de aplicación `runId`, `requestedBy` y la lista de `legal_entity_id` leídos | P6 |
| SLV-R3 | Una corrida lee master data y ATP "as of" `requestedAt`; no relee durante la búsqueda | [+] |
| SLV-R4 | Si falta un dato obligatorio (sin carril para un destino, tipo sin peso con SLV-S1 activa) la corrida no falla: el trozo afectado queda en déficit con `reason` en el log y en el evento (`deficits[].reason`) | [+] |
| SLV-R5 | Una corrida que termina por excepción publica `FAILED` con `error`; nunca termina sin evento salvo caída del proceso | [+] |
| SLV-R6 | Tras reinicio del proceso, las corridas en curso se pierden. `operations` muestra como "sin resultado" una corrida sin evento tras `timeLimitSeconds + 120 s` (lo calcula la UI a partir de la respuesta 202) | [+] |
| SLV-R7 | Reproducibilidad: con la misma entrada y `randomSeed` fijo (`environmentMode = REPRODUCIBLE` en tests), el resultado es idéntico | [+] |

## §8 Estructura y convenciones Kotlin `[+]`

| Tema | Elección |
|---|---|
| JDK / lenguaje | JDK 21, Kotlin 2.x, Gradle Kotlin DSL |
| Framework | Spring Boot 3.x (`spring-boot-starter-web`, `-security` + `oauth2-resource-server`, `-actuator`, `-jdbc`) |
| OpenAPI | `springdoc-openapi` (el documento generado se compara con `solver.yaml` en el job `conformance`) |
| Solver | `ai.timefold.solver:timefold-solver-spring-boot-starter` (Community) |
| PostgreSQL | `NamedParameterJdbcTemplate` + driver `org.postgresql`. Sin JPA |
| ClickHouse | `com.clickhouse:clickhouse-jdbc` |
| Kafka | `spring-kafka` + `io.confluent:kafka-avro-serializer`; clases Avro con `com.github.davidmc24.gradle.plugin.avro` desde `contracts/events/solver` |
| Tests | JUnit 5, `ConstraintVerifier`, Testcontainers (PostgreSQL, ClickHouse, Kafka) |
| Observabilidad | OpenTelemetry Java agent, mismo OTLP que .NET |

```
solver/src/main/kotlin/ch/nussli/nexus/solver/
├── api/          RunController, RunRequest/RunStatus DTO, seguridad
├── input/        ReservationReader (PG), MasterDataReader (PG), SupplyCurveReader (CH), ProblemFactory
├── domain/       DemandChunk, SupplyLocation, … , NexusConstraintProvider, Solution
├── run/          RunService (SolverManager), RunRegistry (en memoria)
└── output/       RunCompletedPublisher (Kafka), ProposalAggregator, DeficitAdvisor
```

## §9 Pantallas

Las pantallas de planificación están en `logistics.md §9` (paso 4.5): lanzar corrida, progreso (sondeo de `GET /v1/runs/{id}` cada 2 s), resultado desde `operations`.

## §10 Abiertos ⚑

- **SLV-Q1:** ¿se planifican también reservas `SOFT` por defecto? Propuesta: no (`includeSoft = false`); solo bajo petición para escenarios.
- **SLV-Q2:** la clase de vehículo por defecto es única por corrida. Mezclar clases por carril es una extensión.
- **SLV-Q3:** penalización de déficit uniforme. Alternativa: ponderar por `replacement_value` o por prioridad del proyecto.
