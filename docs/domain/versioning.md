# Dominio · Versionado de master data (A10)

- **Origen:** A10 (P10), VR1, VR2
- **Esquema:** no tiene tablas propias; define el patrón que aplican `catalog`, `controlling` y `access`
- **Pasos:** 1.4 (infraestructura genérica + primeras tablas). Se reutiliza en 5.6. `access.role_permission` (0.6) es una variante temporal más simple (ACC-R7)

## §1 Principio

**VR1:** en una tabla versionable nunca se hace `UPDATE` sobre un campo versionado. Se inserta una fila nueva con `valid_from = now()` y se cierra la anterior con `valid_to = now()`. La clave de negocio (`business_key`) es estable entre versiones; el `id` cambia en cada versión.

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
-- Trigger genérico [+]: en una fila versionada solo se permite cerrar la versión vigente.
-- Permitido: valid_to (NULL → valor) y superseded_by_id (NULL → valor), una sola vez.
-- Cualquier otro cambio, o cualquier DELETE, lanza 'versioned row is immutable'.
CREATE OR REPLACE FUNCTION infra.guard_versioned_row() RETURNS trigger ...;

CREATE TRIGGER trg_<t>_versioned
  BEFORE UPDATE OR DELETE ON <schema>.<t>
  FOR EACH ROW EXECUTE FUNCTION infra.guard_versioned_row();
```

La función se crea en 1.4 (`R__infra__guard_versioned_row.sql`). Compara `to_jsonb(OLD) - '{valid_to,superseded_by_id,updated_at,updated_by}'` con lo mismo de `NEW`.

## §4 Operación de versionado

Un único caso de uso genérico en `BuildingBlocks.Persistence` (`VersionedRepository<T>`), usado por cada servicio:

1. `SELECT ... WHERE business_key = $1 AND valid_to IS NULL FOR UPDATE` (bloquea la vigente).
2. Si los valores nuevos son iguales a los vigentes → no hace nada y devuelve la vigente (VER-R4).
3. `t = now()` (de `TimeProvider`, igual para las dos filas).
4. `INSERT` nueva fila: `id` nuevo, mismo `business_key`, `version_no = vigente + 1`, `valid_from = t`, `valid_to = NULL`.
5. `UPDATE` vigente: `valid_to = t`, `superseded_by_id = <id nuevo>`.
6. Evento `nexus.<contexto>.<aggregate>.versioned` con la versión nueva completa y `previousVersionId`.

Alta de un `business_key` nuevo: `version_no = 1`, `superseded_by_id = NULL`. Baja lógica: se cierra la vigente (`valid_to = t`) sin sucesora y se publica `.versioned` con `isRetired = true`.

## §5 Reglas

| Id | Regla | Origen |
|---|---|---|
| VER-R1 | Una fila versionada no cambia salvo el cierre (`valid_to`, `superseded_by_id`) de la versión vigente | VR1 |
| VER-R2 | Para un `business_key` hay como máximo una versión vigente y las ventanas no se solapan (`EXCLUDE`) | VR1 |
| VER-R3 | `version_no` es consecutivo por `business_key`, empezando en 1 | A10 |
| VER-R4 | Guardar sin cambios no crea versión | [+] |
| VER-R5 | La consulta "as of" `t` devuelve la fila con `valid_from <= t AND (valid_to IS NULL OR valid_to > t)` | A10 |
| VER-R6 | Los FK desde documentos transaccionales a una tabla VR1 apuntan al `id` de la **versión** usada (trazabilidad exacta), además de copiar el valor (VR2) | [+] |
| VER-R7 | `valid_from` siempre es `now()`. No hay versiones con fecha futura | A10 ⚑ VER-Q1 |

## §6 API (patrón por recurso versionado)

| Método y ruta | Uso |
|---|---|
| `GET /v1/<recurso>?asOf=<instante>` | Lista "as of" (por defecto, ahora) |
| `GET /v1/<recurso>/{businessKey}` | Versión vigente |
| `GET /v1/<recurso>/{businessKey}/versions` | Cadena completa, de la más reciente a la más antigua |
| `GET /v1/<recurso>/{businessKey}/versions/{versionNo}` | Una versión |
| `POST /v1/<recurso>/{businessKey}/versions` | Crea versión nueva (cuerpo = valores completos). `If-Match` con el `ETag` de la vigente |
| `POST /v1/<recurso>/{businessKey}/retire` | Cierra sin sucesora |

`businessKey` viaja codificado para URL. No hay `PUT` sobre recursos versionados.

## §7 Eventos

`nexus.<contexto>.<aggregate>.versioned` (RC8), estado completo de la versión nueva + `previousVersionId` + `isRetired`. Lo consumen proyecciones de ClickHouse para escenarios históricos (A10) y las réplicas de otros servicios.

## §8 Pantallas

`nx-version-history` (`shared/platform`, paso 1.4): línea de tiempo de versiones con fecha, autor y diferencias campo a campo entre versiones consecutivas; selector "ver como estaba el <fecha>".

## §9 Abiertos ⚑

- **VER-Q1:** VR1 fija `valid_from = now()`. En alquiler es habitual publicar una tarifa nueva "a partir del 1 de enero". Con VR1 literal, alguien tiene que crear la versión ese día. Opción: permitir `valid_from` futuro solo en `asset_type_price_rule`, manteniendo la no-superposición. Requiere decisión antes de 1.4.
