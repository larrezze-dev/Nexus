# Convenciones de tests `[+]`

## §1 Niveles

| Nivel | Qué prueba | Herramienta | Dónde |
|---|---|---|---|
| Unidad de dominio | Invariantes de agregados y value objects. Sin BD | xUnit v3 + Shouldly | `<Servicio>.Tests/Domain` |
| Integración | Handler + PostgreSQL real + Outbox + RLS | Testcontainers PostgreSQL con migraciones Flyway aplicadas | `<Servicio>.Tests/Integration` |
| Mensajería | Consumidor + Kafka + inbox | Testcontainers Kafka + Schema Registry | `<Servicio>.Tests/Messaging` |
| Proyección | Consumidor → ClickHouse | Testcontainers ClickHouse | `Nexus.Projections.Tests` |
| Conformidad | OpenAPI expuesto = contrato | oasdiff en CI | `tests/contract` |
| Componente UI | Páginas y componentes Angular | Runner del CLI | `web/` |
| e2e | Flujos de la ficha de punta a punta | Playwright contra docker-compose | `tests/e2e` |
| Solver | Restricciones y puntuación | `ConstraintVerifier` de Timefold | `solver/src/test` |

## §2 Trazabilidad de reglas

- Cada regla `XXX-Rn` que una ficha manda implementar tiene **al menos un test** cuyo nombre o atributo la cita:

```csharp
[Fact(DisplayName = "CAT-R2: tracking_mode cannot change once items exist")]
```

- `make rule-coverage STEP=<id>` (script creado en 0.1) lista las reglas de la ficha sin test. Gate D exige que la lista esté vacía.

## §3 Tests obligatorios por tipo de artefacto

| Artefacto | Tests mínimos |
|---|---|
| Tabla con RLS | (a) usuario con alcance en LE-A no ve filas de LE-B; (b) no puede insertar con `legal_entity_id` de LE-B (`WITH CHECK`); (c) sin `app.current_user_id` no ve nada |
| Tabla append-only | `UPDATE` y `DELETE` fallan con el rol del servicio y con el propietario |
| Tabla versionada | Nueva versión cierra la anterior; no hay solapes; consulta "as of" devuelve la versión correcta en 3 fechas |
| Comando que emite evento | (a) el evento queda en `outbox.message` en la misma transacción; (b) si el handler falla después de escribir, no hay fila ni evento |
| Máquina de estados | Cada transición permitida funciona; al menos una transición prohibida devuelve 409 `invalid_state` |
| Consumidor | Procesar dos veces el mismo `event_id` produce un único efecto |
| Endpoint | 403 sin permiso; 404 para un id de otro Mandant (nunca 403) |

## §4 Datos de prueba

- `Nexus.BuildingBlocks.Testing` ofrece `OrgFixture` con un grupo, dos entidades legales (LE-A, LE-B), un almacén en cada una y tres usuarios: `admin` (GLOBAL), `userA` (LE-A), `whA` (solo almacén de LE-A).
- Los builders (`AssetTypeBuilder`, etc.) generan datos válidos por defecto con Bogus; el test solo fija lo relevante.
- Cada test de integración corre en una transacción que se revierte, o con base limpia por clase de test. No hay estado compartido entre tests.

## §5 Rendimiento (desde Fase 3)

- Escenario base del ledger: 10 M filas en `stock_movement` sembradas con un script; la consulta de saldo por almacén y tipo debe responder en < 200 ms p95 en local. Se mide en 3.1 y se repite en 4.3.
- No se optimiza sin medición previa.
