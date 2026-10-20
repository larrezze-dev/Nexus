# ADR-0001 · Migraciones SQL-first con Flyway

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** 0.3 y todos los pasos con track C

## Contexto
El esquema de NEXUS depende de piezas que EF Core no modela bien: políticas RLS, roles con `BYPASSRLS`, funciones `SECURITY DEFINER`, constraints `EXCLUDE` con `btree_gist`, triggers append-only y grants por rol. Con migraciones de EF, todo eso acabaría como SQL crudo dentro de clases C#. Además, las migraciones son contrato (Gate C) y tienen que revisarse como SQL.

## Decisión
- El esquema se define en SQL plano en `db/migrations`, aplicado con **Flyway** (Community).
- Versionadas `V<YYYYMMDDHHMM>__<contexto>__<desc>.sql`; repetibles `R__<contexto>__<objeto>.sql` para funciones, vistas y políticas.
- EF Core se usa solo como ORM de escritura, mapeando a un esquema existente. **Nunca** genera migraciones.
- ClickHouse: mismo criterio en `db/clickhouse`, aplicado por `nexus-projections`.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Migraciones EF Core | RLS, roles y constraints avanzados acaban en SQL crudo; el esquema se revisa como código C# |
| DbUp | Válido, pero sin repetibles ni `validate` de checksums de serie |
| Sqitch | Menos conocido; añade Perl al toolchain |

## Consecuencias
- El DDL se revisa en Gate C tal como se ejecutará.
- El mapeo EF debe coincidir con el esquema; un test de integración por servicio lo verifica (cada `DbSet` hace una consulta vacía sin errores).
- Flyway corre en contenedor; no requiere JVM en las máquinas de desarrollo.

## Cómo se verifica
Job `db` de CI: `flyway migrate` + `flyway validate` sobre PostgreSQL limpio. Búsqueda en el repositorio: ningún archivo `*Migration*.cs` de EF.
