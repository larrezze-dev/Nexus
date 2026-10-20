# Dominio · Tier de captura móvil (A3)

- **Origen:** A3 (P3), Parte 3 §18, D3 (Android nativo), D4 (Kotlin), P6 (`FIELD_CAPTURE`), ADR-0006 (identidad), ADR-0009 (ids del cliente)
- **Esquema:** no tiene tablas de negocio propias. Usa `warehouse`, `commissioning` y `field` a través de la API de `nexus-field`; registra dispositivos en `warehouse.mobile_device` `[+]`
- **Servicio:** app `mobile/` (Kotlin) + API `/v1/mobile/*` en `nexus-field` · **Contrato:** `contracts/openapi/field.yaml` (etiqueta `mobile`)
- **Pasos:** 6.4 (base offline-first, auth, sync, dispositivo), 6.5 (flujos de almacén y campo, RFID)

## §1 Principio

La app **no es una capa de datos nueva**: es un **tier de captura edge** que alimenta §10 (almacén) y §12 (campo) mediante la resolución de `AssetIdentifier`. Tres propiedades:

1. **Offline-first:** todo escaneo se guarda primero en el dispositivo y se sube después. La app funciona sin red durante una jornada completa (objetivo: 72 h).
2. **Sincronización idempotente:** cada registro nace con un UUIDv7 del dispositivo (ADR-0009). Subirlo dos veces produce un único efecto (WHS-R8).
3. **Semántica según contexto:** en almacén, un escaneo es `warehouse.scan_event`; en obra, `field.field_scan_event`. El usuario elige el contexto (o lo fija la sesión) y la app envía al endpoint correspondiente.

El servidor es la autoridad: el resultado que muestra la app sin red (`ACCEPTED`, `NOT_EXPECTED`…) es **provisional** y se sustituye por el del servidor al sincronizar.

## §2 Arquitectura de la app `[+]`

```
mobile/app/src/main/kotlin/ch/nussli/nexus/mobile/
├── core/
│   ├── auth/        OIDC (AppAuth), almacenamiento seguro de tokens, refresco
│   ├── api/         cliente generado (field.yaml), interceptores (token, correlación, idioma)
│   ├── db/          Room: espejo local + cola de salida
│   ├── sync/        SyncEngine (WorkManager): descarga delta, subida por lotes, reintentos
│   ├── scan/        BarcodeSource (cámara ML Kit | DataWedge de la PDA), RfidReader (adaptador del fabricante)
│   └── ui/          tema, componentes, i18n
├── warehouse/       por cargar/descargar, sesión de carga
├── field/           envíos, descarga en obra, instalación, daños/faltas, recuento en obra
└── progress/        fases y progreso con fotos
```

| Tema | Elección |
|---|---|
| Lenguaje / build | Kotlin 2.x, Gradle Kotlin DSL, JDK 21 |
| SDK | `minSdk` 30 ⚑ MOB-Q1 (depende de las PDA), `targetSdk` el último estable |
| UI | Jetpack Compose + Material 3 |
| DI | Hilt |
| Base local | Room |
| Trabajo en segundo plano | WorkManager |
| HTTP | Retrofit + OkHttp + `kotlinx.serialization`; cliente generado con `openapi-generator` (`kotlin`, librería `jvm-retrofit2`) |
| Auth | AppAuth-Android (OIDC genérico: Entra ID en producción, Keycloak en local; ADR-0006) |
| Cámara / códigos | CameraX + ML Kit Barcode Scanning; en PDA con lector integrado, intents de DataWedge (Zebra) o equivalente |
| RFID | SDK del fabricante detrás de `RfidReader` ⚑ MOB-Q1 |
| Tests | JUnit 5, Turbine, Robolectric, Compose UI tests; MockWebServer para la API |

```kotlin
interface RfidReader {
    val state: StateFlow<ReaderState>                 // DISCONNECTED, CONNECTED, READING, ERROR
    suspend fun connect(): Result<Unit>
    fun inventory(power: Int): Flow<RfidTag>          // lecturas continuas; la app deduplica por EPC
    suspend fun stop()
}
data class RfidTag(val epcHex: String, val rssi: Int, val readAt: Instant)
```

## §3 Base local (Room)

| Tabla local | Contenido | Origen |
|---|---|---|
| `identifier` | `type`, `value`, `targetType`, `assetTypeId`, `assetItemId`, `batchId`, `unitsPerScan` | delta de sync |
| `asset_type_lite` | id, código, nombre en el idioma del usuario, `trackingMode` | delta de sync |
| `transfer_task` | transferencias por cargar/descargar del alcance del usuario + líneas | delta de sync |
| `load_session_local` | sesiones abiertas en el dispositivo | local + servidor |
| `job_phase_lite` | fases de los Jobs de los proyectos asignados | delta de sync |
| `outbox_item` | cola de salida: `id` (UUIDv7), `kind` (`SCAN`, `FIELD_SCAN`, `SESSION_OPEN`, `SESSION_CLOSE`, `PROGRESS`, `PHOTO`, `ARRIVE`), `payload`, `createdAt`, `attempts`, `lastError`, `serverResult` | local |
| `sync_cursor` | último cursor por colección | local |

Las fotos se guardan como fichero local y se suben como documento (`POST /api/platform/v1/documents`) antes del registro que las referencia (`PHOTO` precede en la cola) `[+]`.

## §4 Protocolo de sincronización

### Descarga (delta)

`GET /v1/mobile/sync?collections=identifiers,assetTypes,transferTasks,jobPhases&cursor=<opaco>`

- Devuelve, por colección, `upserts[]`, `deletes[]` y `nextCursor`. El cursor codifica `(updated_at, id)` por colección.
- **Alcance:** lo que el usuario puede ver: almacenes visibles (`access.visible_warehouses()`) y proyectos con `FIELD_CAPTURE` vigente o visibles. Los identificadores se limitan a los del material implicado en las tareas del alcance (líneas de transferencias abiertas y `site_inventory` de sus proyectos) más los tipos `BULK` del catálogo `[+]` ⚑ MOB-Q3.
- Primera sincronización: sin cursor. Si el cursor es demasiado antiguo (> 7 días) → 410 y la app hace una completa.

### Subida (por lotes)

| Endpoint | Cuerpo | Respuesta por elemento |
|---|---|---|
| `POST /v1/mobile/load-sessions` | `{id, transferId, sessionType}` | `CREATED` · `EXISTS` (mismo id, mismo contenido) · `CONFLICT` |
| `POST /v1/mobile/scan-events:batch` | hasta 500 escaneos de almacén | `{id, status: ACCEPTED_NEW|DUPLICATE_ID|REJECTED, result, reason?}` |
| `POST /v1/mobile/field-scan-events:batch` | hasta 500 escaneos de campo | ídem |
| `POST /v1/mobile/load-sessions/{id}/close` | `{closedAt}` | estado de la sesión |
| `POST /v1/mobile/progress-logs:batch` | registros de progreso | ídem |
| `POST /v1/mobile/shipments/{id}/arrive` | `{id (cliente), arrivedAt}` | |

- La cola se procesa **en orden de creación** por sesión (abrir → escaneos → cerrar). Un elemento `REJECTED` no bloquea los siguientes salvo `SESSION_OPEN` rechazado, que marca la sesión local como fallida.
- Reintentos con backoff exponencial (WorkManager), máximo 20; después queda en "Requiere atención" en la UI.
- Tamaño máximo de petición: 2 MB.

### Dispositivo

```sql
-- [+] paso 6.4, esquema warehouse. RLS: GLOBAL (lo administra IT); solo svc_field
CREATE TABLE warehouse.mobile_device (
  id               uuid PRIMARY KEY,               -- generado en el primer arranque, persistente
  device_label     text NOT NULL,                  -- p. ej. 'PDA-CH-07'
  model            text NOT NULL,
  os_version       text NOT NULL,
  app_version      text NOT NULL,
  rfid_capable     boolean NOT NULL DEFAULT false,
  registered_by    uuid NOT NULL,
  last_seen_at     timestamptz NOT NULL,
  last_user_id     uuid NULL,
  is_blocked       boolean NOT NULL DEFAULT false,
  -- + std
  UNIQUE (device_label)
);
```

`POST /v1/mobile/devices/register` en el primer arranque; cada sincronización actualiza `last_seen_at`, `app_version` y `last_user_id`. Un dispositivo `is_blocked` recibe 403 `device_blocked` en toda la API móvil.

## §5 Reglas

| Id | Regla | Origen |
|---|---|---|
| MOB-R1 | Todo registro creado en el dispositivo lleva UUIDv7 generado en el dispositivo y `scannedAt`/`loggedAt` con su reloj | A3, ADR-0009 |
| MOB-R2 | La app nunca descarta un escaneo: si el servidor lo rechaza, queda visible con su motivo | A3 |
| MOB-R3 | El resultado local es provisional; tras sincronizar se muestra el del servidor (`serverResult`) | [+] |
| MOB-R4 | RFID: en una lectura continua, cada EPC se envía una sola vez por sesión (deduplicación local) | [+] |
| MOB-R5 | Sin `FIELD_CAPTURE` vigente en un proyecto, la app no lo muestra ni permite escanear en él; si caduca con datos pendientes, la subida se intenta igual y el servidor decide (403 → "Requiere atención") | P6 |
| MOB-R6 | El token se refresca en segundo plano; si el refresco falla, la app sigue capturando offline y pide login al recuperar red, sin perder la cola | [+] |
| MOB-R7 | Versión mínima: el servidor devuelve `X-Min-App-Version`; una app inferior solo puede sincronizar la cola pendiente y pide actualizar | [+] |
| MOB-R8 | Los datos locales se cifran en reposo (base Room con SQLCipher o cifrado del sistema) y se borran al cerrar sesión, salvo la cola pendiente, que se conserva ligada al usuario ⚑ | [+] |

## §6 API móvil (`field.yaml`, etiqueta `mobile`)

| Método y ruta | Permiso |
|---|---|
| `POST /v1/mobile/devices/register` | autenticado |
| `GET /v1/mobile/sync` | autenticado (filtra por alcance) |
| `GET /v1/mobile/me` (usuario, idioma, almacenes, proyectos con `FIELD_CAPTURE`, permisos) | autenticado |
| Subidas de §4 | `LOAD_SESSION:*`, `FIELD_SCAN:CREATE`, `JOB_PHASE:UPDATE`, `SHIPMENT:UPDATE` según el elemento |

## §7 Pantallas de la app

| Pantalla | Paso |
|---|---|
| Login, selección de idioma, estado de sincronización (pendientes, última sincronización, errores) | 6.4 |
| Inicio: tareas de hoy por almacén/proyecto | 6.4 |
| Escaneo de prueba: resolver un identificador (QR por cámara) | 6.4 |
| Almacén: por cargar / por descargar → sesión (esperado vs escaneado, QR/código/RFID, deshacer, cerrar) | 6.5 |
| Campo: envíos del proyecto → llegada → descarga en obra; instalación/desmontaje por escaneo; daño/falta con foto; recuento en obra; carga de retorno | 6.5 |
| Progreso de fase con fotos | 6.5 |

## §8 Abiertos ⚑

- **MOB-Q1:** modelo de PDA y lector UHF RFID (Zebra, Chainway, …): define `minSdk`, el SDK concreto y si el lector de códigos es por DataWedge. **Bloquea 6.5** (RFID). 6.4 se hace con cámara.
- **MOB-Q3:** volumen de identificadores en la descarga: si un almacén tiene cientos de miles de ítems, se limita a las tareas abiertas y se resuelve en línea lo demás.
- **MOB-Q4:** gestión de dispositivos (MDM: Intune u otro) para distribución de la app y borrado remoto.
