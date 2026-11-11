# Dominio · Versionado de master data (A10)

- **Origen:** A10 (P10), VR1, VR2
- **Esquema:** no tiene tablas propias; define el patrón que aplican `catalog`, `controlling` y `access`
- **Pasos:** 1.4 (infraestructura genérica + primeras tablas). Se reutiliza en 5.6. `access.role_permission` (0.6) es una variante temporal más simple (ACC-R7)

## §1 Principio

**VR1:** en una tabla versionable nunca se hace `UPDATE` sobre un campo versionado. Se inserta una fila nueva con `valid_from = t` y se cierra la anterior con `valid_to = t`. La clave de negocio (`business_key`) es estable entre versiones; el `id` cambia en cada versión.

**Fecha de efecto `[+]` (VER-Q1, decidido 2026-11-11):** `t` es "ahora" (el instante de la transacción, §3) por defecto, pero se puede programar una versión con fecha futura (`t ≥` ahora, `infra.app_now()`) en **cualquier** tabla VR1. Principio: **el pasado nunca cambia; el futuro se puede editar.** Una versión nueva con fecha `t` cierra la que esté vigente justo antes de `t` y **anula** las versiones programadas desde `t`. Las anuladas se quedan en la tabla con ventana vacía y no se borran (VER-R8).

**Consecuencia para todo consumidor `[+]`:** `valid_to IS NULL` ya no significa "vigente", sino "última de la cadena", que puede estar programada. Lo vigente en un instante se obtiene **siempre** con la consulta "as of" (VER-R5): ATP, Solver, explosión de BOM, checklists, resolución de tarifas y réplicas. Nunca se toma sin más la última versión.

**VR2:** versionar la tabla maestra **no** sustituye el *snapshot* en el documento transaccional. `QuoteLine.unit_price` y `BudgetLine.planned_unit_cost` siguen copiando el valor en el momento de la transacción. VR1 sirve para auditar y consultar la historia de la tabla maestra; el snapshot sirve para que un contrato firmado no cambie de precio.

**P10:** el BOM "tal como era" en la fecha de un contrato se obtiene con la consulta "as of" sobre `catalog.asset_type_bom`. No hay snapshot de BOM en `QuoteLine`.

## §2 Qué se versiona

| Tabla | Prioridad (A10) | Forma | Paso |
|---|---|---|---|
| `catalog.asset_type_price_rule` | Alta | VR1 completo (fila entera) | 1.4 |
| `catalog.asset_type_bom` | Alta | VR1 completo, una fila por línea de BOM | 1.4 |
| `controlling.cost_concept_reserve_pct` `[+]` | Media | VR1 sobre el atributo `default_reserve_pct`, en tabla hija | 5.6 |
| `access.role_permission` | Media | Ventana temporal sin `business_key` ni `version_no` (ACC-R7) | 0.6 |
| `hr.role_rate` `[+]` | Media | VR1 completo. Las horas aprobadas apuntan a la versión usada (VER-R6) | 5.4 |
| `access.role_assignment` | — | Ya tiene `valid_from`/`valid_to` desde el diseño original | 0.6 |
| `docs.document_type` | No | — | — |

**Por qué el porcentaje de reserva va en tabla hija `[+]`:** `cost_concept` tiene identidad estable (las `BudgetLine` y `CostPosting` lo referencian por FK). Solo `default_reserve_pct` cambia con consecuencia económica. Versionar la fila entera obligaría a que las FK apunten a una versión concreta.

## §3 Columnas y constraints

Plantilla en `conventions-database.md §8` (`EXCLUDE` diferido hasta el `COMMIT`, índice parcial de la última versión sin `UNIQUE`). Además:

**Reloj de la transacción `[+]` (decidido 2026-11-11):** el `UnitOfWork` fija al abrir la transacción `set_config('nexus.now', $1, true)` (local a la transacción, como `app.current_user_id`) con el instante de `TimeProvider`, y lo expone a la aplicación como el instante de la transacción. En la base de datos, `infra.app_now()` devuelve `coalesce(nullif(current_setting('nexus.now', true), '')::timestamptz, now())`: en una conexión reutilizada del pool la variable vale `''`, no `NULL` (mismo patrón que `app.current_user_id` en `R__access__scope_functions.sql`). El guard y `VersionedRepository<T>` usan **ese mismo instante** ("ahora" en este documento): no hay dos relojes y los tests con `FakeTimeProvider` pueden probar fechas pasadas y futuras.

**Estados de una fila `[+]`:** *anulada* si `valid_to = valid_from` (ventana vacía; nunca cumple la consulta "as of" ni vuelve a cambiar); *viva* en otro caso.

```sql
-- Trigger genérico [+]: en una fila versionada solo cambian valid_to y superseded_by_id,
-- y solo en el futuro (VER-R8): coalesce(OLD.valid_to,'infinity') >= infra.app_now()
-- y coalesce(NEW.valid_to,'infinity') >= infra.app_now(). Una fila anulada no cambia.
-- Cualquier otro cambio, mover valid_to hacia el pasado o cualquier DELETE lanza
-- 'versioned row is immutable'.
CREATE OR REPLACE FUNCTION infra.guard_versioned_row() RETURNS trigger ...;

CREATE TRIGGER trg_<t>_versioned
  BEFORE UPDATE OR DELETE ON <schema>.<t>
  FOR EACH ROW EXECUTE FUNCTION infra.guard_versioned_row();
```

La función y `infra.app_now()` se crean en 1.4 (`R__infra__guard_versioned_row.sql`). El guard compara `to_jsonb(OLD) - '{valid_to,superseded_by_id,updated_at,updated_by}'` con lo mismo de `NEW`, y aplica la condición de arriba cuando cambia `valid_to` o `superseded_by_id`.

## §4 Operación de versionado

Un único caso de uso genérico en `BuildingBlocks.Persistence` (`VersionedRepository<T>`), usado por cada servicio `[+]` (pasos reescritos el 2026-11-11 por VER-Q1 y su revisión):

1. **Fecha de efecto:** `t` = `validFrom` pedido, o "ahora" si no se indica. `t <` ahora → 422 `VER-R7`.
2. **Bloqueo de la cadena:** `SELECT id FROM <t> WHERE business_key = $1 AND version_no = 1 FOR UPDATE`. Serializa todos los comandos sobre el mismo `business_key` y da el `nexus-aggregate-id` (ADR-0020). Si no existe, es un alta (`version_no = 1`): dos altas simultáneas chocan en `uq_<t>_version` → 409.
3. **Lectura de la cadena** (sentencia nueva, después del bloqueo; solo filas vivas):
   - `P` = la viva con `valid_from < t AND coalesce(valid_to, 'infinity') >= t`: la vigente justo antes de `t` (puede ser ninguna).
   - `S` = las vivas con `valid_from >= t`: las programadas que esta operación anula.
   - `If-Match` se compara con el `ETag` de la fila con `max(version_no)`.
4. **Sin cambios (VER-R4):** si `S` está vacío y `P` existe con los mismos valores (incluido `is_retired`) → no hace nada y devuelve `P`.
5. `INSERT` nueva fila: `id` nuevo, mismo `business_key`, `version_no = max(version_no) + 1` (de la cadena entera, anuladas incluidas), `valid_from = t`, `valid_to = NULL`.
6. `UPDATE` de `P`: `valid_to = t`, `superseded_by_id = <id nuevo>`. Si `P.valid_to` ya era `t` (su sucesora en `t` se anula ahora), solo cambia `superseded_by_id`.
7. `UPDATE` de cada fila de `S`: `valid_to = valid_from` (anulada), `superseded_by_id = <id nuevo>` (VER-R8).
8. Evento `nexus.<contexto>.<aggregate>.versioned` con la versión nueva completa, `previousVersionId` (= `P`), `annulledVersionIds` (= `S`). Cabeceras según ADR-0020.

El orden INSERT → UPDATE es el que genera EF Core (la fila referenciada por `superseded_by_id` va primero). Funciona porque el `EXCLUDE` es diferido: la no-superposición se comprueba en el `COMMIT`, que es donde salta un conflicto residual (23P01 → 409, igual que `uq_<t>_version`; ambos se registran en el mapa de constraints de `NexusExceptionHandler` y el `UnitOfWork` traduce también los fallos del `COMMIT`).

**Alta** de un `business_key` nuevo: `version_no = 1`, `superseded_by_id = NULL`; admite `validFrom`. Un alta (`POST /v1/<recurso>`) para un `business_key` que ya existe, aunque esté de baja → 409, remite a `/versions`.

**Baja lógica `[+]` (ADR-0020):** es una versión más, con `is_retired = true` y los valores de `P` copiados. Sigue los pasos 1–8 y admite `validFrom`. Sin `P`, o con `P` ya de baja → 422 `VER-R9` (no aplica a la versión que crea "Anular programada"). Volver a dar de alta un `business_key` dado de baja es otra versión con `is_retired = false`.

**Anular una versión programada `[+]` (decidido 2026-11-11):** `POST …/versions/{versionNo}/annul`, solo para una versión viva con `valid_from >` ahora. Crea una versión con `t` = `valid_from` de la anulada y los valores de `P`. Si no hay `P` (era un alta programada), crea una baja (`is_retired = true`) con los valores de la anulada. Esta versión no pasa por VER-R9: restaura el estado anterior, aunque sea "no existe" o "de baja". Como cualquier versión con fecha `t`, anula también las programadas posteriores a `t` (pasos 3 y 7). No hay mecánica nueva en la base de datos.

## §5 Reglas

| Id | Regla | Origen |
|---|---|---|
| VER-R1 | Una fila versionada no cambia salvo `valid_to` y `superseded_by_id`, y solo en el futuro (VER-R8) | VR1 |
| VER-R2 | Para un `business_key` hay como máximo una versión vigente y las ventanas no se solapan (`EXCLUDE`, comprobado en el `COMMIT`) | VR1 |
| VER-R3 | `version_no` es consecutivo por `business_key`, empezando en 1. Las versiones anuladas y las de baja cuentan, y ningún número se reutiliza `[+]` | A10, ADR-0020 |
| VER-R4 | Guardar sin cambios no crea versión. La comparación incluye `is_retired` y solo aplica si no hay programadas posteriores a `t` | [+] |
| VER-R5 | La consulta "as of" `t` devuelve la fila con `valid_from <= t AND (valid_to IS NULL OR valid_to > t)`. Si esa fila tiene `is_retired`, el recurso no existe en `t`: toda consulta de existencia o de valores vigentes filtra `NOT is_retired` (explosión de BOM, `resolve` de tarifas, CAT-R11, Solver, ATP, réplicas). Es la **única** forma válida de obtener la versión vigente (no `valid_to IS NULL`) | A10, [+] |
| VER-R6 | Los FK desde documentos transaccionales a una tabla VR1 apuntan al `id` de la **versión** usada (trazabilidad exacta), además de copiar el valor (VR2). Nunca a una versión con `is_retired` ni anulada en el momento de crear el documento `[+]` | [+] |
| VER-R7 | `valid_from` es "ahora" o una fecha futura indicada por el usuario (`validFrom`), en cualquier tabla VR1. Una fecha pasada → 422 `VER-R7` | [+] VER-Q1 (2026-11-11) |
| VER-R8 | El futuro es editable: una versión con fecha `t` cierra la vigente justo antes de `t` y anula las programadas vivas con `valid_from >= t` (`valid_to = valid_from`, ventana vacía, nunca visible en "as of"). Nada que ya haya sido vigente cambia: `valid_to`/`superseded_by_id` solo se mueven si el `valid_to` viejo y el nuevo son `>=` ahora (`infra.app_now()`) | [+] VER-Q1 (2026-11-11) |
| VER-R9 | La baja (`/retire`) necesita una versión vigente justo antes de `t` que no esté de baja; copia sus valores. No aplica a la versión que crea "Anular programada" (§4) | [+] ADR-0020 |
| VER-R10 | Si la versión a la que apunta un documento se anula después, el documento conserva su snapshot (VR2) y su FK. La pantalla del documento muestra el aviso "versión de origen anulada" (con el `status` de la versión, §6), y el responsable decide si recalcula a mano. No hay recálculo automático | [+] (decidido 2026-11-11) |

## §6 API (patrón por recurso versionado)

| Método y ruta | Uso |
|---|---|
| `GET /v1/<recurso>?asOf=<instante>` | Lista "as of" (por defecto, ahora), sin las de baja |
| `GET /v1/<recurso>/{businessKey}` | Versión vigente ahora (VER-R5). 404 si está de baja o si solo hay un alta programada. Incluye `scheduled` (versiones programadas vivas) `[+]` |
| `GET /v1/<recurso>/{businessKey}/versions` | Cadena completa, de la más reciente a la más antigua |
| `GET /v1/<recurso>/{businessKey}/versions/{versionNo}` | Una versión. Cada versión lleva `status` derivado: `ACTIVE`, `SCHEDULED`, `SUPERSEDED`, `ANNULLED`, `RETIRED` `[+]` |
| `POST /v1/<recurso>/{businessKey}/versions` | Crea versión nueva (cuerpo = valores completos + `validFrom?`, por defecto ahora). `If-Match` con el `ETag` de la última de la cadena. La respuesta incluye `annulledVersionIds` `[+]` |
| `POST /v1/<recurso>/{businessKey}/retire` | Baja (versión con `isRetired`). Cuerpo: `validFrom?`. `If-Match` igual que arriba `[+]` |
| `POST /v1/<recurso>/{businessKey}/versions/{versionNo}/annul` `[+]` | Anula una versión programada (§4). `If-Match` igual que arriba |

`businessKey` viaja codificado para URL. No hay `PUT` sobre recursos versionados, salvo el BOM, que recibe la lista completa de un kit (`catalog.md §6`).

**`validFrom` `[+]`:** instante ISO-8601 con offset y precisión de microsegundos (ADR-0016). La UI convierte una fecha elegida en el selector a las 00:00 de su zona horaria (`Europe/Zurich`, `conventions-frontend.md §7`). Para "ahora" omite el campo, así no recibe 422 por el desfase del reloj del cliente.

## §7 Eventos

`nexus.<contexto>.<aggregate>.versioned` (RC8), estado completo de la versión nueva + `validFrom` `[+]` + `previousVersionId` + `isRetired` + `annulledVersionIds` `[+]`. Se publica al crear la versión, no cuando entra en vigor. Cabeceras: ADR-0020. Lo consumen proyecciones de ClickHouse para escenarios históricos (A10) y las réplicas de otros servicios.

## §8 Pantallas

`nx-version-history` (`shared/platform`, paso 1.4): línea de tiempo de versiones con fecha, autor y diferencias campo a campo entre versiones consecutivas; selector "ver como estaba el <fecha>". Las programadas se muestran como "programada para <fecha>" con el botón "Anular programada", y las anuladas, tachadas `[+]`. El formulario de versión nueva y la baja llevan el campo opcional "Fecha de efecto" `[+]`.

## §9 Abiertos ⚑

- **VER-Q1 (decidido 2026-11-11):** `valid_from` futuro permitido en **todas** las tablas VR1 (VER-R7), y el futuro es editable (VER-R8). Decisión de Luciano: él eligió la opción general frente a la restringida a tarifas, y después "el futuro es editable" frente a "lo programado es intocable" y "anular con DELETE". La baja como fila y las cabeceras de los eventos: ADR-0020. Revisión del 2026-11-11: el reloj lo fija la aplicación (`nexus.now`, §3), el BOM se anula por línea (`catalog.md §6`), hay una acción explícita "Anular programada" y los documentos conservan su snapshot con aviso (VER-R10).
