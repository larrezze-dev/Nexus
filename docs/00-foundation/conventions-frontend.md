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

## §4 Sistema de diseño y patrones de pantalla `[+]`

Aprobado por Luciano el 2026-11-12 (canvas «NEXUS — Sistema de diseño web», https://claude.ai/artifact/8giDWSAii8cRFoTQi7qDjH; el texto de esta sección es normativo aunque no se tenga acceso al canvas). Toda pantalla nueva o rehecha sigue esta sección; las skills `.claude/skills/nx-ui-*` la desarrollan con código.

### §4.1 Fundamentos
- **Tokens:** el aspecto se controla con `web/src/styles/tokens.css` (`--nx-*`), `components.css` y `theme.scss` (ver `web/src/styles/README.md`). Los componentes no usan hex. La paleta corporativa sigue ⚑ abierta: el azul primario es provisional.
- **Color:** fondo de página gris neutro `#F6F7F9`, tarjetas y barras blancas con borde `#DDE1E6`, texto `#1B1C1F`, texto atenuado `#5A6068`, un único color de acción (primary), error `#BA1A1A`. Las superficies Material son neutras (sin el tinte del tema M3). Chips de estado con texto siempre visible, contraste AA.
- **Tipografía:** Roboto autoalojada (`@fontsource/roboto` 400/500/700, `@fontsource/roboto-mono` 400; ADR-0010). Pila `Roboto, "Helvetica Neue", Arial, sans-serif` en tema y tokens. Escala: título de página 28/36 500, título de sección 18/24 500, texto y tabla 14/20, etiqueta 12/16, código 13 mono.
- **Espaciado** 4/8/12/16/24/32 (`--nx-space-1..6`); radios 4 (campos), 8 (avisos), 12 (tarjetas).
- **Botones:** un único primario (relleno) por vista: «Nuevo …» en la lista, «Guardar cambios» en el detalle, «Crear …» en el alta. Secundario de contorno, texto para «Cancelar», destructivo de contorno rojo con diálogo de confirmación (servicio `NxConfirm`, nunca `confirm()` del navegador). Nunca botones, fechas, tablas ni checkboxes nativos sin Material.
- **Campos:** `mat-form-field` con `appearance="outline"` en toda la app (por defecto global con `MAT_FORM_FIELD_DEFAULT_OPTIONS`); etiqueta siempre dentro del campo, ayuda (`mat-hint`) debajo.

### §4.2 Shell
- Barra superior blanca de 56 px: ☰, logo, selector de idioma visible (nombre del idioma en su idioma), menú de usuario con nombre, «Mi cuenta» y «Cerrar sesión». Sin buscador global mientras no tenga función.
- Menú lateral de 248 px, agrupado (Organización · Personas y terceros · Accesos), elemento activo marcado. ☰ lo oculta del todo y lo vuelve a mostrar en cualquier ancho. A partir de 1280 px está abierto por defecto y la elección se recuerda en el navegador; por debajo de 1280 px está siempre oculto al entrar y se abre por encima del contenido.
- **Idioma:** al entrar, el preferido del usuario (`/me`); el selector cambia toda la interfaz al momento (textos, captions de datos, fechas, paginador, grid) y guarda la elección como preferencia del usuario (`PATCH /v1/me`). `<html lang>` y `document.title` («Página · NEXUS») siguen al idioma.
- **Migas** encima del título, siempre: sección › lista › registro. El último nivel es el nombre del registro (o «Nuevo X»), no es enlace y nunca repite la lista.

### §4.3 Rejilla y anchos
- Margen de contenido 32 px (24 px por debajo de 1280 px). Las listas usan todo el ancho; detalle y formulario, máximo 1120 px.
- Formularios en rejilla de 12 columnas (separación 24 px horizontal, 16 px vertical). Cada campo ocupa un tramo: S = 3 (código, país), M = 4, L = 6 (nombres), XL = 12 (textos largos). Nunca anchos fijos en px. Por debajo de 1280 px, 6 columnas; por debajo de 600 px, 1 columna.

### §4.4 Lista (`*-list.page`)
- Migas · cabecera (título, número de registros, «Nuevo …» a la derecha, visible solo con permiso `CREATE`).
- Tarjeta con barra de filtros arriba (búsqueda `q` y los filtros que ofrezca el contrato, con la etiqueta dentro del campo; «Limpiar filtros»), `nx-data-grid` y paginador traducido abajo.
- La fila entera abre el registro (clic o Enter). La tabla muestra todas las filas de la página sin cortes, o hace scroll dentro con la cabecera fija. Columna de estado con `nx-status-badge`. Códigos en mono.
- Por debajo de 600 px la tabla pasa a tarjetas (nombre, código, estado) y «Nuevo» es un botón flotante.

### §4.5 Detalle (`*-detail.page`)
- Cabecera en una fila: nombre del registro (título) con chip de estado, y debajo el código y la fecha de última modificación; acciones de transición a la derecha (Desactivar en estilo destructivo).
- Pestañas sin estirar, en este orden: **General** · [Versiones] · [Documentos] · [Aprobaciones] · **Auditoría**.
- General = secciones con título (`nx-form-section`) sobre la rejilla §4.3. Los selectores de entidad muestran «código + nombre» del valor elegido dentro del propio campo. Los campos que no se pueden cambiar se ven en solo lectura con candado y una ayuda que dice por qué.
- Barra de acciones al pie, a la derecha: «Cancelar» (texto) · «Guardar cambios» (primario). Un registro no editable (SYSTEM, sin permiso) no tiene Guardar activo ni controles editables.
- Guardado con `If-Match`; un 409 muestra el aviso «Otro usuario ha modificado este registro» con «Recargar». Un guardado correcto muestra un aviso breve.

### §4.6 Formulario de alta (`/new`)
- Mismo esqueleto que el detalle sin pestañas: migas «… › Nuevo X», título, frase que explica qué es, secciones con título, ayuda bajo cada campo cuando aporta.
- Un formulario de más de 4 secciones lleva un índice de secciones fijo a la derecha (`nx-section-index`; desaparece por debajo de 1280 px).
- Errores al guardar: aviso arriba con el número de campos a revisar y mensaje en cada campo. Tras crear, se abre el detalle con el aviso «X creado».
- Reactive Forms tipados. Los textos traducibles usan `nx-i18n-caption-editor` (una fila por idioma, `de` obligatorio; en móvil, de/en visibles y el resto bajo «Mostrar más idiomas»).

### §4.7 Estados
Toda vista tiene diseñados: cargando (filas esqueleto, nunca «Loading…»), vacío (icono, frase, acción si procede), sin resultados con filtros («Limpiar filtros»), error (frase de usuario + «Reintentar»), sin permiso (403, dentro del shell), no encontrado (404 y ruta inexistente, dentro del shell) y no se pudo iniciar sesión (`/unauthorized`, pantalla completa fuera del shell).

### §4.8 Textos
Lenguaje de usuario final: nada de UUIDs, `true/false`, códigos de permiso, nombres de campo del API ni referencias a pasos. Enums y permisos traducidos («Auditoría — Ver»). Vocabulario del glosario («Entidad legal»; en `de`, «Gesellschaft», «Lager»). Datos de ejemplo siempre ficticios (empresa «SAPE smart bud», «Centro I+D Barcelona»), nunca datos reales de clientes.

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
| `nx-page`, `nx-page-header`, `nx-form-section`, `nx-form-grid`, `nx-action-bar`, `nx-filter-bar`, `nx-section-index`, `nx-empty-state` (variantes vacío, sin resultados, error, 403, 404) y el servicio `NxConfirm` (diálogo de confirmación) `[+]` | UI 2026-11 | Plantillas de §4 (`shared/ui/layout/`) |
| Pipe `nxCaption`, `NxBreadcrumbService`, clases `.nx-alert`, `.nx-card` `[+]` | UI 2026-11 | Caption en el idioma activo; nombre del registro en las migas; avisos y tarjetas |
| `nx-audit-trail` | 0.8 | Eventos del agregado (endpoint `GET /v1/audit/{aggregateType}/{aggregateId}` sobre la proyección `event_log`) |

## §6 Permisos en la UI

- `GET /api/platform/v1/me` devuelve el usuario, su idioma y la lista de permisos con su alcance.
- La directiva `*nxCan="'ASSET_TYPE:CREATE'"` oculta acciones sin permiso. **Es solo cosmética**: el servidor decide.

## §7 Números, fechas e idiomas

- Importes y cantidades llegan como string. Se muestran con `Intl.NumberFormat` del idioma del usuario. **No se hace aritmética de dinero en el cliente**; los totales los calcula el servidor.
- Fechas en zona `Europe/Zurich` para mostrar, UTC en la API.
- Textos de la UI en `public/assets/i18n/*.json`; claves `feature.page.element`. Los textos de datos (`caption_i18n`) vienen del API.
- `[+]` Patrones obligatorios (skill `nx-ui-i18n`): cabeceras de grid reactivas al idioma (nunca `translate()` síncrono al construir), `MatPaginatorIntl` y `localeText` de AG Grid traducidos, `DateAdapter` con la configuración regional del idioma y `mat-datepicker` (nunca `<input type="date">` nativo), enums con clave propia, `caption_i18n` mostrado con el pipe `nxCaption` (idioma activo → `de` → `en` → primero disponible). Una clave nueva entra en los 5 idiomas en el mismo commit (quien crea la clave escribe las 5 traducciones; `nx-mechanic` puede revisarlas después). Grupos permitidos: los existentes (`common`, `shared`, `nav`, `shell`, `breadcrumb`, `errors`, `httpPages`, `account`, `org`, `access`, `audit`, `events`) y `pageTitle`; `locations.*` y `warehouses.*` se migran a `org.*`.

## §8 Calidad

- ESLint con `@angular-eslint`, Prettier.
- Accesibilidad: WCAG 2.1 AA en componentes compartidos.
- Cada página tiene al menos un test de componente; cada flujo de la ficha, un test e2e.
