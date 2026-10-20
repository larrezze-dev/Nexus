# Convenciones de frontend (Angular) `[+]`

Base: D5 (Angular/TypeScript) y ADR-0010 (librerías de UI).

## §1 Plataforma

| Tema | Elección |
|---|---|
| Angular | Última versión estable al ejecutar 0.5; se fija en `package.json` y solo cambia por ADR |
| Componentes | Standalone. Sin `NgModule` propios |
| Estado | Signals + un *facade* por feature (`<feature>.facade.ts`). Sin NgRx salvo ADR |
| Detección de cambios | La que el CLI genere por defecto en esa versión (zoneless si lo es) |
| UI | Angular Material + AG Grid Community para tablas de datos + Apache ECharts (`ngx-echarts`) para gráficos |
| Autenticación | `angular-auth-oidc-client` (OIDC genérico: Entra ID en Azure, Keycloak en local; ADR-0006) |
| i18n | Transloco, cambio de idioma en caliente. Idiomas: `de`, `en`, `fr`, `it`, `es` |
| Cliente API | Generado con `ng-openapi-gen` desde `contracts/openapi/*.yaml` en `src/app/api/<servicio>/` |
| Tests | Runner por defecto del CLI instalado; e2e con Playwright (`tests/e2e`) |

## §2 Desarrollo contra el contrato (track F)

El frontend no espera al backend:

1. `make contracts` genera el cliente Angular desde el contrato aprobado en Gate C.
2. `make mock-api S=<servicio>` levanta Prism con los `example` del contrato.
3. `ng serve --configuration mock` usa `proxy.mock.json`, que dirige `/api/<servicio>` a Prism.
4. `ng serve` (configuración por defecto) usa `proxy.conf.json`, que apunta al gateway en `:8080`.

- **FE-R1:** ningún componente llama a `HttpClient` directamente. Solo a los servicios generados, a través del *facade*.
- **FE-R2:** el código generado no se edita. Si no sirve, el contrato está mal y vuelve a Gate C.

## §3 Estructura de una feature

```
features/catalog/
├── catalog.routes.ts            # rutas lazy
├── asset-types/
│   ├── asset-type-list.page.ts
│   ├── asset-type-detail.page.ts
│   ├── asset-type-form.component.ts
│   └── asset-types.facade.ts    # signals: items, selected, loading, error; métodos que llaman al API generado
└── …
```

## §4 Patrones de pantalla

Todas las pantallas de master data siguen dos plantillas, para que el agente no diseñe cada una de cero.

**Lista** (`*-list.page`):
- Barra superior: título, búsqueda `q`, filtros declarados en la ficha, botón "Nuevo" (visible solo con permiso `CREATE`).
- `nx-data-grid` (envoltorio de AG Grid) con paginación de servidor (§4 de `conventions-api.md`), orden por columna y selección.
- Columna de estado con `nx-status-badge`.

**Detalle** (`*-detail.page`):
- Cabecera con código, nombre traducido, estado y acciones de transición (botones según permisos y estado).
- Pestañas: **General** (formulario), y las que apliquen: **Versiones** (`nx-version-history`, A10), **Documentos** (`nx-document-panel`, A9), **Aprobaciones** (`nx-approval-panel`, A8), **Auditoría** (`nx-audit-trail`).
- Guardado con `If-Match`. Un 409 muestra "Otro usuario ha modificado este registro" y ofrece recargar.

**Formularios:** Reactive Forms tipados. Los textos traducibles usan `nx-i18n-caption-editor` (una fila por idioma, `de` obligatorio).

## §5 Componentes compartidos (`shared/`)

| Componente | Paso en que se crea | Uso |
|---|---|---|
| `nx-data-grid` | 0.5 | Todas las listas |
| `nx-status-badge` | 0.5 | Estados de cualquier máquina de estados |
| `nx-i18n-caption-editor` | 0.4 | Campos `caption_i18n` |
| `nx-entity-picker` | 0.4 | Selector con búsqueda de cualquier entidad (LE, almacén, tipo, proyecto…) |
| `nx-money` / `nx-quantity` | 1.2 | Mostrar y editar importes y cantidades con unidad, como string decimal |
| `nx-approval-panel` | 0.9 | Estado y acciones de una `ApprovalRequest` sobre cualquier recurso |
| `nx-document-panel` | 0.10 | Documentos de cualquier recurso |
| `nx-version-history` | 1.4 | Cadena de versiones de una entidad versionada |
| `nx-audit-trail` | 0.8 | Eventos del agregado (endpoint `GET /v1/audit/{aggregateType}/{aggregateId}` sobre la proyección `event_log`) |

## §6 Permisos en la UI

- `GET /api/platform/v1/me` devuelve el usuario, su idioma y la lista de permisos con su alcance.
- La directiva `*nxCan="'ASSET_TYPE:CREATE'"` oculta acciones sin permiso. **Es solo cosmética**: el servidor decide.

## §7 Números, fechas e idiomas

- Importes y cantidades llegan como string. Se muestran con `Intl.NumberFormat` del idioma del usuario. **No se hace aritmética de dinero en el cliente**; los totales los calcula el servidor.
- Fechas en zona `Europe/Zurich` para mostrar, UTC en la API.
- Textos de la UI en `assets/i18n/*.json`; claves `feature.page.element`. Los textos de datos (`caption_i18n`) vienen del API.

## §8 Calidad

- ESLint con `@angular-eslint`, Prettier.
- Accesibilidad: WCAG 2.1 AA en componentes compartidos.
- Cada página tiene al menos un test de componente; cada flujo de la ficha, un test e2e.
