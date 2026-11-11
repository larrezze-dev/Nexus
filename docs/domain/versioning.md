# Dominio · Versionado de master data (A10)

- **Origen:** A10 (P10), VR1, VR2
- **Esquema:** no tiene tablas propias; define el patrón que aplican `catalog`, `controlling` y `access`
- **Pasos:** 1.4 (infraestructura genérica + primeras tablas). Se reutiliza en 5.6. `access.role_permission` (0.6) es una variante temporal más simple (ACC-R7)

## §1 Principio

**VR1:** en una tabla versionable nunca se hace `UPDATE` sobre un campo versionado. Se inserta una fila nueva con `valid_from = t` y se cierra la anterior con `valid_to = t`. La clave de negocio (`business_key`) es estable entre versiones; el `id` cambia en cada versión.

**Fecha de efecto `[+]` (VER-Q1, decidido 2026-11-11):** `t` es `now()` por defecto, pero se puede programar una versión con fecha futura (`t ≥ now()`) en **cualquier** tabla VR1. Principio: **el pasado nunca cambia; el futuro se puede editar.** Una versión nueva con fecha `t` cierra la que esté vigente en `t` y **anula** las versiones programadas posteriores a `t`. Las anuladas se quedan en la tabla con ventana vacía y no se borran (VER-R8).

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

Plantilla en `conventions-database.md §8`. Además:

```sql
-- Trigger genérico [+]: en una fila versionada solo cambian valid_to y superseded_by_id,
-- y solo en el futuro (VER-R8): el valor viejo de valid_to (NULL = infinito) y el nuevo
-- tienen que ser >= now(). superseded_by_id solo cambia junto con valid_to.
-- Cualquier otro cambio, mover valid_to hacia el pasado o cualquier DELETE lanza
-- 'versioned row is immutable'.
CREATE OR REPLACE FUNCTION infra.guard_versioned_row() RETURNS trigger ...;

CREATE TRIGGER trg_<t>_versioned
  BEFORE UPDATE OR DELETE ON <schema>.<t>
  FOR EACH ROW EXECUTE FUNCTION infra.guard_versioned_row();
```

La función se crea en 1.4 (`R__infra__guard_versioned_row.sql`). Compara `to_jsonb(OLD) - '{valid_to,superseded_by_id,updated_at,updated_by}'` con lo mismo de `NEW`, y comprueba `coalesce(OLD.valid_to, 'infinity') >= now() AND coalesce(NEW.valid_to, 'infinity') >= now()` cuando `valid_to` cambia. `now()` es el inicio de la transacción, el mismo instante que usa el paso 3 de §4.

## §4 Operación de versionado

Un único caso de uso genérico en `BuildingBlocks.Persistence` (`VersionedRepository<T>`), usado por cada servicio:

1. `SELECT ... WHERE business_key = $1 AND coalesce(valid_to, 'infinity') > $t FOR UPDATE`: bloquea la versión vigente en `t` (si la hay) y todas las programadas posteriores.
2. Si no hay programadas posteriores a `t` y los valores nuevos son iguales a los de la versión vigente en `t` → no hace nada y la devuelve (VER-R4).
3. `t` = fecha de efecto pedida, o `now()` si no se indica (de `TimeProvider`, igual para todas las filas de la operación). `t < now()` → 422 `VER-R7`.
4. `INSERT` nueva fila: `id` nuevo, mismo `business_key`, `version_no = max(version_no) + 1` (de la cadena entera, anuladas incluidas), `valid_from = t`, `valid_to = NULL`.
5. `UPDATE` de la vigente en `t`: `valid_to = t`, `superseded_by_id = <id nuevo>`.
6. `UPDATE` de cada programada posterior a `t` (`valid_from > t`): `valid_to = valid_from` (ventana vacía, anulada), `superseded_by_id = <id nuevo>` (VER-R8).
7. Evento `nexus.<contexto>.<aggregate>.versioned` con la versión nueva completa, `previousVersionId` y `annulledVersionIds`. Cabeceras según ADR-0020: `nexus-aggregate-id` = `id` de la versión 1 de la cadena; `nexus-aggregate-version` = `version_no`.

Alta de un `business_key` nuevo: `version_no = 1`, `superseded_by_id = NULL`. **Baja lógica `[+]` (ADR-0020):** es una versión más, con `is_retired = true` y los valores de la anterior copiados. Sigue los mismos pasos 1–7 (también puede programarse) y se publica `.versioned` con `isRetired = true`. Volver a dar de alta un `business_key` dado de baja es otra versión con `is_retired = false`.

## §5 Reglas

| Id | Regla | Origen |
|---|---|---|
| VER-R1 | Una fila versionada no cambia salvo `valid_to` y `superseded_by_id`, y solo en el futuro (VER-R8) | VR1 |
| VER-R2 | Para un `business_key` hay como máximo una versión vigente y las ventanas no se solapan (`EXCLUDE`) | VR1 |
| VER-R3 | `version_no` es consecutivo por `business_key`, empezando en 1. Las versiones anuladas y las de baja cuentan, y ningún número se reutiliza | A10, ADR-0020 |
| VER-R4 | Guardar sin cambios no crea versión | [+] |
| VER-R5 | La consulta "as of" `t` devuelve la fila con `valid_from <= t AND (valid_to IS NULL OR valid_to > t)`. Si esa fila tiene `is_retired`, el recurso no existe en `t`. Es la **única** forma válida de obtener la versión vigente (no `valid_to IS NULL`) | A10, [+] |
| VER-R6 | Los FK desde documentos transaccionales a una tabla VR1 apuntan al `id` de la **versión** usada (trazabilidad exacta), además de copiar el valor (VR2) | [+] |
| VER-R7 | `valid_from` es `now()` o una fecha futura indicada por el usuario (`validFrom`), en cualquier tabla VR1. Una fecha pasada → 422 `VER-R7` | [+] VER-Q1 (2026-11-11) |
| VER-R8 | El futuro es editable: una versión con fecha `t` cierra la vigente en `t` y anula las programadas con `valid_from > t` (`valid_to = valid_from`, ventana vacía, nunca visible en "as of"). Nada que ya haya sido vigente cambia: `valid_to` solo se mueve si su valor viejo y el nuevo son `>= now()` | [+] VER-Q1 (2026-11-11) |

## §6 API (patrón por recurso versionado)

| Método y ruta | Uso |
|---|---|
| `GET /v1/<recurso>?asOf=<instante>` | Lista "as of" (por defecto, ahora) |
| `GET /v1/<recurso>/{businessKey}` | Versión vigente ahora (VER-R5); 404 si está de baja. Incluye `scheduled` (versiones programadas pendientes) `[+]` |
| `GET /v1/<recurso>/{businessKey}/versions` | Cadena completa, de la más reciente a la más antigua |
| `GET /v1/<recurso>/{businessKey}/versions/{versionNo}` | Una versión |
| `POST /v1/<recurso>/{businessKey}/versions` | Crea versión nueva (cuerpo = valores completos + `validFrom?`, por defecto ahora). `If-Match` con el `ETag` de la última de la cadena. La respuesta indica las versiones anuladas |
| `POST /v1/<recurso>/{businessKey}/retire` | Baja (versión con `isRetired`). Cuerpo: `validFrom?`. `If-Match` igual que arriba |

`businessKey` viaja codificado para URL. No hay `PUT` sobre recursos versionados.

## §7 Eventos

`nexus.<contexto>.<aggregate>.versioned` (RC8), estado completo de la versión nueva + `validFrom` + `previousVersionId` + `isRetired` + `annulledVersionIds` `[+]`. Se publica al crear la versión, no cuando entra en vigor. Cabeceras: ADR-0020. Lo consumen proyecciones de ClickHouse para escenarios históricos (A10) y las réplicas de otros servicios.

## §8 Pantallas

`nx-version-history` (`shared/platform`, paso 1.4): línea de tiempo de versiones con fecha, autor y diferencias campo a campo entre versiones consecutivas; selector "ver como estaba el <fecha>". Las programadas se muestran como "programada para <fecha>" y las anuladas, tachadas `[+]`. El formulario de versión nueva y la baja llevan el campo opcional "Fecha de efecto" `[+]`.

## §9 Abiertos ⚑

- **VER-Q1 (decidido 2026-11-11):** `valid_from` futuro permitido en **todas** las tablas VR1 (VER-R7), y el futuro es editable (VER-R8). Decisión de Luciano: él eligió la opción general frente a la restringida a tarifas, y después "el futuro es editable" frente a "lo programado es intocable" y "anular con DELETE". La baja como fila y las cabeceras de los eventos: ADR-0020.
