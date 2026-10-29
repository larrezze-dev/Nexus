# Registro de ADR

Decisiones de implementación que los manuales no cubren. Mientras un ADR esté en **Propuesto**, el agente no implementa nada que dependa de él.

Antes de lanzar 0.1 tienen que estar en `Aceptado` los ADR 0001–0010. El 0011 se cierra dentro de 0.11; el 0012, antes de 4.4.

| ADR | Título | Estado | Bloquea |
|---|---|---|---|
| [0001](0001-sql-first-migrations.md) | Migraciones SQL-first con Flyway | Aceptado | 0.3 |
| [0002](0002-single-database-schema-per-context.md) | Una base PostgreSQL, un esquema por contexto | Aceptado | 0.3 |
| [0003](0003-org-core-in-phase-0.md) | Núcleo organizativo en Fase 0 (S10) | Aceptado | 0.4 |
| [0004](0004-access-scope-evaluated-per-statement.md) | `EffectiveAccessScope` evaluado por sentencia, sin materializar | Aceptado | 0.6 |
| [0005](0005-contract-first-parallel-tracks.md) | Contract-first y tracks paralelos | Aceptado | 0.1 |
| [0006](0006-authentication-entra-id.md) | Autenticación con Microsoft Entra ID | Aceptado | 0.5 |
| [0007](0007-dotnet-base-libraries.md) | Librerías base .NET sin licencias comerciales | Aceptado | 0.1 |
| [0008](0008-avro-event-format.md) | Avro como formato de eventos | Aceptado | 0.7 |
| [0009](0009-uuidv7-application-generated.md) | UUIDv7 generados en aplicación | Aceptado | 0.3 |
| [0010](0010-frontend-ui-libraries.md) | Librerías de UI del frontend | Aceptado | 0.5 |
| [0011](0011-azure-hosting.md) | Hosting en Azure (cómputo, Kafka, ClickHouse) | Propuesto ⚑ abierto | 0.11 |
| [0012](0012-solver-planning-model.md) | Interfaz y modelo de planificación del Solver | Propuesto | 4.4 |
| [0013](0013-outbox-payload-column-type.md) | Tipo de columna para `outbox.message.payload` | Aceptado | 0.3, implementar antes de 0.4 |
| [0014](0014-openapi-conformance-vs-fluentvalidation.md) | Conformidad OpenAPI del servicio vs. reglas de FluentValidation | Propuesto ⚑ abierto | 0.4 (track B5), todo paso con FluentValidation |

Para un ADR nuevo: copiar `0000-template.md`, siguiente número libre, estado `Propuesto`, y añadir la fila aquí.
