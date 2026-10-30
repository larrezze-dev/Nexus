# Convenciones de API (REST + OpenAPI) `[+]`

## §1 Rutas

- Externa (vía gateway): `/api/<servicio>/v1/<recurso>`. El gateway quita `/api/<servicio>` y enruta al servicio.
- Interna (servicio): `/v1/<recurso>`.
- Recursos en plural y kebab-case: `/v1/asset-types`, `/v1/legal-entities/{id}/business-units`.
- Transiciones de estado: `POST /v1/<recurso>/{id}/<verbo>` (`/v1/jobs/{id}/confirm`). Nunca `PATCH` del campo `status`.

| Operación | Método | Respuesta |
|---|---|---|
| Listar | `GET /v1/x` | 200 + página (§4) |
| Detalle | `GET /v1/x/{id}` | 200 + `ETag` |
| Crear | `POST /v1/x` | 201 + `Location` + cuerpo |
| Modificar | `PUT /v1/x/{id}` con `If-Match` | 200 + nuevo `ETag` |
| Transición | `POST /v1/x/{id}/<verbo>` con `If-Match` | 200 |
| Desactivar | `POST /v1/x/{id}/deactivate` | 200. **No hay `DELETE`** en master data |

## §2 Contratos OpenAPI

- OpenAPI 3.1, un archivo por servicio en `contracts/openapi/<servicio>.yaml`.
- `operationId` en camelCase: `<verbo><Recurso>` (`createAssetType`, `confirmJob`). El cliente Angular usa estos nombres.
- Esquemas: `<Recurso>` (detalle), `<Recurso>ListItem`, `Create<Recurso>Request`, `Update<Recurso>Request`.
- Spectral valida el contrato en CI con las reglas de `contracts/openapi/.spectral.yaml` (creadas en 0.1).

## §3 Extensiones obligatorias

| Extensión | Dónde | Contenido |
|---|---|---|
| `x-permission` | Cada operación | `RESOURCE:ACTION`, p. ej. `ASSET_TYPE:CREATE`. `PUBLIC` solo en `/health`. `AUTHENTICATED` `[+]` para un endpoint que solo exige sesión válida, sin permiso concreto (p. ej. `GET /v1/me`, paso 0.5) |
| `x-scope` | Operaciones con alcance | De dónde sale el alcance del permiso: `body.legalEntityId`, `path.projectId`, `none` (sin alcance: `has_permission` se llama sin argumentos de entidad — ACC-R10 restringe esto a listados/detalle), `global` `[+]` (paso 0.6: el actor debe tener el permiso en una asignación `GLOBAL`, no en cualquier alcance; para catálogos sin Mandant cuya escritura no debe poder delegarse a un alcance más estrecho, p. ej. `ROLE:CREATE`/`ROLE:UPDATE`) |
| `x-rules` | Operaciones con reglas de negocio | Lista de ids de regla que puede devolver en 422 |

## §4 Paginación, orden y filtros

**Listados de master data** (pocos miles de filas): offset.

```
GET /v1/asset-types?page=1&pageSize=50&sort=code,-updatedAt&q=bühne&isActive=true
→ { "items": [...], "page": 1, "pageSize": 50, "total": 1234 }
```

`pageSize` máximo 200.

**Listados de alto volumen** (ledger, escaneos, logs): cursor por clave.

```
GET /v1/stock-movements?limit=100&cursor=<opaco>&warehouseId=...&from=...&to=...
→ { "items": [...], "nextCursor": "<opaco>|null" }
```

Los filtros son parámetros explícitos declarados en el contrato. No hay lenguaje de filtro genérico.

## §5 Formatos

| Dato | JSON |
|---|---|
| IDs | string UUID |
| Instantes | string ISO-8601 UTC (`2026-10-18T16:20:00Z`) |
| Fechas | string `YYYY-MM-DD` |
| Importes y cantidades | **string decimal** (`"1250.5000"`). Nunca `number`: JavaScript pierde precisión |
| Moneda | `{"amount":"1250.5000","currency":"CHF"}` |
| Cantidad con unidad | `{"value":"12.0000","uomId":"..."}` |
| Texto traducible | objeto `{"de":"...","en":"..."}` completo; la UI elige el idioma |
| Nombres de propiedad | camelCase |

- **Concurrencia:** `GET` devuelve `ETag: "<xmin>"`. `PUT` y transiciones exigen `If-Match`; sin él → 428, distinto → 409.
- **Idempotencia en altas:** los `POST` de alta aceptan un `id` opcional generado por el cliente (UUIDv7). Si ya existe con el mismo contenido → 200 con el recurso; si existe con contenido distinto → 409. La app móvil siempre envía `id` (sincronización idempotente, A3).

## §6 Errores

Formato y códigos: `conventions-backend.md §7`. El contrato declara las respuestas 400, 401, 403, 404, 409 y 422 que aplican a cada operación con `$ref` a `#/components/responses/*` comunes.

## §7 Consultas analíticas (ClickHouse)

ClickHouse no conoce al usuario. Toda consulta analítica:

1. Entra por un endpoint de un servicio .NET (no hay acceso directo a ClickHouse desde el frontend).
2. El servicio obtiene en PostgreSQL, dentro de la transacción de la petición, `access.visible_legal_entities()` (y warehouses/projects si aplica).
3. `BuildingBlocks.Analytics` añade `WHERE legal_entity_id IN (...)` como parámetro, nunca concatenado.

Un test de arquitectura impide usar el cliente de ClickHouse fuera de `BuildingBlocks.Analytics`.

## §8 Mock para el frontend

`make mock-api S=<servicio>` arranca Prism sobre `contracts/openapi/<servicio>.yaml` en `4010 + n`. Cada operación del contrato lleva al menos un `example` realista en la respuesta 200/201: es lo que verá el frontend mientras el backend no existe.
