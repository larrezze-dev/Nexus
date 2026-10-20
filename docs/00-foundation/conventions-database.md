# Convenciones de base de datos (PostgreSQL)

Todo lo de este documento es `[+]` salvo donde se cita el manual. Las decisiones de fondo están en ADR-0001 (SQL-first), ADR-0002 (esquema por contexto), ADR-0004 (alcance RLS) y ADR-0009 (UUIDv7).

## §1 Generalidades

| Tema | Convención |
|---|---|
| Versión | PostgreSQL 17 (local y Azure Database for PostgreSQL Flexible Server). Cambiar de mayor requiere ADR |
| Base | Una sola: `nexus` |
| Esquemas | Uno por contexto (`architecture.md §3`) |
| Nombres | `snake_case`, tablas en singular (`asset_type`), columnas en singular |
| Clave primaria | `id uuid PRIMARY KEY`, UUIDv7 generado en la aplicación (ADR-0009). Sin `DEFAULT` en BD |
| Clave foránea | `<entidad>_id`, siempre con índice |
| Códigos de negocio | `code text NOT NULL`, único dentro de su ámbito, `CHECK (code ~ '^[A-Z0-9_\-\.]{1,40}$')` |
| Booleanos | `is_<x>` / `has_<x>` / `requires_<x>`, `NOT NULL DEFAULT false` |
| Enumeraciones | `text` + `CHECK (col IN (...))`. No se usan tipos `ENUM` de PostgreSQL (son difíciles de migrar) |
| Extensiones | `btree_gist` (exclusión temporal), `pg_trgm` (búsqueda) |

## §2 Roles y permisos

| Rol | Tipo | Permisos | RLS |
|---|---|---|---|
| `nexus_owner` | Sin login | Propietario de todos los objetos. Solo lo usa Flyway | Propietario (con `FORCE ROW LEVEL SECURITY` en las tablas, también aplica) |
| `nexus_migrator` | Login | Miembro de `nexus_owner`. Credencial solo en CI/CD | — |
| `svc_<servicio>` | Login | DML en sus esquemas; `SELECT` en `org`, `uom`, `catalog`; `EXECUTE` en `access.visible_*()`; `INSERT` en `outbox.message`; DML en `inbox.processed_message` | **Aplica** |
| `svc_relay` | Login | `SELECT`, `UPDATE (status, published_at, attempts, last_error)` en `outbox.message` | No hay RLS en `outbox` |
| `svc_solver` | Login, `BYPASSRLS` | Solo `SELECT` en `org`, `uom`, `catalog`, `project`, `stock`, `logistics`; `EXECUTE` en `access.has_permission`/`access.current_user_id`. Ninguna escritura (P6) | Se salta RLS |
| `svc_migration` | Login, `BYPASSRLS` | DML en todos los esquemas. **Solo existe durante Fase 7**; se elimina tras el cutover | Se salta RLS |

Servicios: `svc_platform`, `svc_catalog`, `svc_operations`, `svc_commercial`, `svc_resources`, `svc_controlling`, `svc_field`.

- **DB-R1:** ningún rol de servicio es propietario de tablas.
- **DB-R2:** toda tabla con RLS lleva `ENABLE` y `FORCE ROW LEVEL SECURITY`.
- **DB-R3:** los grants se declaran en la migración que crea la tabla, nunca a posteriori a mano.

## §3 Tipos

| Dato | Tipo |
|---|---|
| Identificador | `uuid` |
| Fecha y hora | `timestamptz` (UTC). Nunca `timestamp` sin zona |
| Fecha de negocio (sin hora) | `date` |
| Rango temporal | `tstzrange` cuando hay exclusión de solapes; si no, `valid_from`/`valid_to` |
| Importe | `numeric(19,4)` + `currency_code char(3)` |
| Cantidad | `numeric(18,4)` |
| Porcentaje | `numeric(7,4)` como fracción (0.1500 = 15 %) |
| Tipo de cambio | `numeric(19,10)` |
| Texto traducible | `jsonb` `caption_i18n` (§5) |
| Payload flexible | `jsonb` (solo donde el manual lo dice: `delta_lines`, `payload` de outbox) |
| Geo | `numeric(9,6)` lat / lon. PostGIS no se instala salvo ADR |

## §4 Columnas estándar

**Tablas mutables** (master data, documentos con estado):

```sql
created_at  timestamptz NOT NULL,
created_by  uuid        NOT NULL,   -- org.app_user.id, o SYSTEM
updated_at  timestamptz NOT NULL,
updated_by  uuid        NOT NULL
```

La concurrencia optimista usa la columna de sistema `xmin`, expuesta en la API como `ETag` (`conventions-api.md §5`).

**Tablas append-only:** `created_at`, `created_by` y nada más de auditoría.

**Usuario SYSTEM:** `org.app_user` con `id = '00000000-0000-7000-8000-000000000001'`, sembrado en 0.4. Lo usan jobs y auto-provisiones (P6: `granted_by = SYSTEM`).

Las columnas estándar **no se repiten** en las DDL de `docs/domain/`: donde aparece `-- + std` el agente añade el bloque mutable; donde aparece `-- + std-append` añade el bloque append-only.

## §5 Textos traducibles

```sql
caption_i18n jsonb NOT NULL
  CHECK (jsonb_typeof(caption_i18n) = 'object' AND caption_i18n ? 'de')
```

- Claves: `de`, `en`, `fr`, `it`, `es`. Obligatoria: `de` (idioma por defecto del grupo) `[+]` ⚑ confirmar.
- Orden de resolución en la UI: idioma del usuario → `de` → `en` → primera clave.

## §6 Tablas append-only (H5)

```sql
REVOKE UPDATE, DELETE, TRUNCATE ON <schema>.<tabla> FROM PUBLIC, svc_<servicio>;

CREATE TRIGGER trg_<tabla>_append_only
  BEFORE UPDATE OR DELETE ON <schema>.<tabla>
  FOR EACH ROW EXECUTE FUNCTION infra.forbid_mutation();
```

`infra.forbid_mutation()` lanza `RAISE EXCEPTION 'append-only table %', TG_TABLE_NAME USING ERRCODE = 'P0001'`. Se crea en 0.3. El trigger cubre también al propietario.

Tablas append-only en NEXUS: `stock.stock_movement`, `logistics.operation_log`, `logistics.solver_run`, `logistics.solver_deficit`, `project.job_state_history`, `catalog.maintenance_record`, `org.exchange_rate`, `workflow.approval_action`, `docs.document_access_log`, `controlling.cost_posting`, `warehouse.scan_event`, `commissioning.field_progress_log`, `field.field_scan_event`, `gdpr.pii_access_log`, `gdpr.anonymization_log` `[+]`, `migration.run_log` `[+]`. La lista canónica la mantiene `db/ci/append-only.txt` (DBC-4). `outbox.message` es un caso aparte: el relay actualiza solo sus columnas de estado y purga lo publicado (`conventions-events.md §4`).

## §7 Row-Level Security (A6)

### Contexto de sesión

Cada transacción de un servicio fija el usuario antes de cualquier consulta:

```sql
SET LOCAL app.current_user_id = '<uuid>';
SET LOCAL app.correlation_id  = '<uuid>';
```

Lo hace el `UnitOfWork` de `Nexus.BuildingBlocks.Persistence` (`conventions-backend.md §4`). **Toda** consulta de un servicio se ejecuta dentro de una transacción, también las lecturas.

### Perfiles RLS

Cada tabla multi-Mandant declara su perfil en la DDL con un comentario `-- RLS: <dimensiones>`, p. ej. `-- RLS: LE+WH`. Un perfil es la lista de **dimensiones de alcance** que la tabla tiene como columnas. La política es la **disyunción** de las dimensiones presentes. Las funciones `access.visible_*()` se definen en `domain/access.md §4`.

| Dimensión | Columna | Término de la política |
|---|---|---|
| `LE` (siempre) | `legal_entity_id` | `legal_entity_id = ANY ((SELECT access.visible_legal_entities()))` |
| `BU` | `business_unit_id` | `business_unit_id = ANY ((SELECT access.visible_business_units()))` |
| `WH` | `warehouse_id` | `warehouse_id = ANY ((SELECT access.visible_warehouses()))` |
| `PRJ` | `project_id` | `project_id = ANY ((SELECT access.visible_projects()))` |

Ejemplo: perfil `LE+WH` → `USING (<término LE> OR <término WH>)`, mismo `WITH CHECK`.

`GLOBAL` = sin RLS. Solo para catálogos de grupo sin Mandant (UoM, taxonomía, roles) y la **estructura organizativa** (`org.group`, `org.legal_entity`, `org.business_unit`, `org.warehouse`, `org.location`).

El `(SELECT …)` envolvente es obligatorio: convierte la función en un *InitPlan* que se evalúa **una vez por sentencia**, no una vez por fila.

**Por qué la estructura organizativa es `GLOBAL`** `[+]`: las funciones `access.visible_*()` leen la jerarquía de `org`. Si esas tablas tuvieran RLS, la política se llamaría a sí misma. Saber que existe un almacén no es sensible; lo sensible son sus movimientos, que sí llevan RLS. La UI filtra las listas de estructura por alcance para no mostrar ruido.

**Esquema `access`:** sus tablas no tienen RLS y los roles de servicio no tienen `SELECT` sobre ellas (salvo `svc_platform`). Los demás servicios solo tienen `EXECUTE` sobre las funciones `access.visible_*()`, que son `SECURITY DEFINER`.

**Semillas y jobs:** el usuario SYSTEM tiene un `RoleAssignment` `GLOBAL` sembrado en 0.6. Las semillas y los jobs abren su transacción con `SET LOCAL app.current_user_id = '<SYSTEM>'`; no hay ningún atajo en las políticas para SYSTEM.

Plantilla:

```sql
ALTER TABLE <s>.<t> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <s>.<t> FORCE  ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_<t> ON <s>.<t>;
CREATE POLICY rls_<t> ON <s>.<t>
  USING      (legal_entity_id = ANY ((SELECT access.visible_legal_entities())))
  WITH CHECK (legal_entity_id = ANY ((SELECT access.visible_legal_entities())));
```

Las políticas van en migraciones **repetibles** `R__<contexto>__rls.sql`.

- **DB-R4:** toda tabla nueva con `legal_entity_id` lleva política en el mismo PR de contrato. El job `db` de CI falla si encuentra una tabla con `legal_entity_id` sin RLS (consulta a `pg_class.relrowsecurity`).

## §8 Tablas versionadas (A10, VR1)

```sql
id               uuid PRIMARY KEY,           -- cambia en cada versión
business_key     text NOT NULL,              -- identidad lógica estable
version_no       integer NOT NULL CHECK (version_no >= 1),
valid_from       timestamptz NOT NULL,
valid_to         timestamptz NULL,           -- NULL = versión vigente
superseded_by_id uuid NULL REFERENCES <misma tabla>(id),
CONSTRAINT uq_<t>_version UNIQUE (business_key, version_no),
CONSTRAINT ck_<t>_window  CHECK (valid_to IS NULL OR valid_to > valid_from),
CONSTRAINT ex_<t>_overlap EXCLUDE USING gist (
    business_key WITH =,
    tstzrange(valid_from, coalesce(valid_to, 'infinity')) WITH &&)
```

- Índice parcial de versión vigente: `CREATE UNIQUE INDEX ... ON <t>(business_key) WHERE valid_to IS NULL`.
- Consulta "as of": `WHERE business_key = $1 AND valid_from <= $2 AND (valid_to IS NULL OR valid_to > $2)`.
- La operación de versionado está en `domain/versioning.md`.

## §9 Restricciones e índices

- Toda FK lleva índice.
- Unicidades de negocio como `UNIQUE` (o índice único parcial si hay `is_active`).
- Estados: `CHECK` con la lista cerrada de la máquina de estados del dominio.
- Coherencias simples en `CHECK` (fechas, cantidades positivas). Las reglas que cruzan tablas van en el dominio .NET, nunca en triggers. La única excepción son los triggers de §6.
- Particionado de tablas de alto volumen (`stock_movement`, `scan_event`, `field_scan_event`, `cost_posting`): no se particiona en el arranque. Si una tabla supera 50 M filas o hay problemas medidos, se abre un ADR ⚑.

## §10 Semillas

| Carpeta | Contenido | Se aplica en |
|---|---|---|
| `db/seed/common/` | Datos obligatorios en todos los entornos: usuario SYSTEM, roles base, UoM de sistema, grupos de coste, `PaymentTerms` inicial | Todos |
| `db/seed/dev/` | Grupo, entidades, almacenes y usuarios de ejemplo | Local y CI |
| `db/seed/test/` | Fixtures mínimos para tests e2e | CI |

Las semillas son idempotentes (`INSERT ... ON CONFLICT DO NOTHING`).

## §11 Numeración de documentos `[+]`

Los códigos legibles (`P-LE-CH-2026-0042`, `PO-LE-CH-2026-00017`, `TR-…`, `INV-…`) se generan con una tabla de contadores **por esquema**, para que cada servicio numere sin tocar esquemas ajenos:

```sql
CREATE TABLE <schema>.number_sequence (
  scope_key  text PRIMARY KEY,         -- '<DOC>:<le_code>:<yyyy>'
  next_value bigint NOT NULL DEFAULT 1
);
```

Obtención, dentro de la transacción del documento:

```sql
INSERT INTO <schema>.number_sequence AS s (scope_key, next_value) VALUES ($1, 2)
ON CONFLICT (scope_key) DO UPDATE SET next_value = s.next_value + 1
RETURNING next_value - 1;
```

- **DB-R5:** la numeración no tiene huecos: el contador se actualiza en la misma transacción que el documento, y un rollback lo revierte también. El precio es que la emisión se serializa por `scope_key` (bloqueo de fila); es aceptable para estos volúmenes. **Nunca** se usa `SEQUENCE` de PostgreSQL para números de documento (no es transaccional). Las facturas asignan número solo al emitir (BIL-R3).
- Un helper `IDocumentNumberGenerator` en `BuildingBlocks.Persistence` encapsula la consulta; recibe el esquema por configuración del servicio.
- Formato del número por documento: lo fija cada `docs/domain/<contexto>.md`.

## §12 Comprobaciones automáticas de esquema (job `db` de CI) `[+]`

Consultas que hacen fallar el job si devuelven filas:

| Id | Comprobación |
|---|---|
| DBC-1 | Tabla con columna `legal_entity_id` sin `relrowsecurity` o sin `relforcerowsecurity` (excepciones listadas en `db/ci/rls-exceptions.txt` con su justificación) |
| DBC-2 | FK cuya tabla destino está en un esquema transaccional distinto del de la tabla origen (ARC-R2) |
| DBC-3 | Columna FK sin índice que empiece por ella |
| DBC-4 | Tabla append-only declarada en §6 sin el trigger `trg_<tabla>_append_only` |
| DBC-5 | Columna `timestamp without time zone`, `real`, `double precision` o `money` |
| DBC-6 | Tabla propiedad de un rol distinto de `nexus_owner` |
| DBC-7 | Tabla listada en `db/ci/versioned.txt` sin el trigger `trg_<tabla>_versioned` o sin el índice parcial `uq_*_current` |
