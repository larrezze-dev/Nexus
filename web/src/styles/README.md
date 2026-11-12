# Estilos de NEXUS web

Todo el aspecto de la interfaz se adapta editando solo CSS. Los valores vienen del sistema de diseño aprobado por Luciano (canvas «Fundamentos», 2026-11-12).

| Fichero | Qué controla |
|---|---|
| `tokens.css` | **Fuente única de los valores**: tokens semánticos NEXUS (`--nx-*`) de color, estados, tipos de evento, diff, radios, espaciado, tipografía y layout. |
| `theme.scss` | Tema Angular Material M3: paleta generadora (primary/tertiary), tipografía, densidad y la redirección de los tokens de sistema `--mat-sys-*` hacia los `--nx-*`. |
| `components.css` | Clases genéricas (`.nx-header-bar`, `.nx-card`, `.nx-empty-state`, `.nx-alert`, `.nx-chip-outline`, `.nx-mono`, `.nx-create--header`/`.nx-create--fab` —botón «Nuevo» que pasa a FAB extendido < 600 px—, `.nx-tab-panel` —contenido de una pestaña de detalle—) que solo usan tokens. |
| `../styles.scss` | Carga las fuentes, importa el tema y fija la base del documento (fondo, texto, `h1`/`h2`). |

Orden de carga (`angular.json` → `styles`): `styles.scss`, `tokens.css`, `components.css`.

## Fuentes

- **Roboto autoalojada**, sin Google Fonts: paquetes `@fontsource/roboto` (pesos 400, 500, 700) y `@fontsource/roboto-mono` (400), versión fijada en `package.json` (aprobados por Luciano, ADR-0010).
- Se cargan con `@import '@fontsource/…/<peso>.css'` al principio de `styles.scss`; el builder de Angular copia los `.woff2` a `media/` en la build. Cada CSS declara todos los subconjuntos con `unicode-range`, así el navegador solo descarga los que usa la página (latín).
- Pilas: `--nx-font-family: Roboto, "Helvetica Neue", Arial, sans-serif` y `--nx-font-mono: "Roboto Mono", ui-monospace, Menlo, monospace`.
- El tema Material usa **la misma pila**: en `theme.scss`, `typography: (plain-family: var(--nx-font-family), brand-family: var(--nx-font-family))`, así todos los `--mat-sys-*-font` apuntan a `--nx-font-family` y ningún componente Material cae en la serif del navegador.
- Para añadir un peso (p. ej. 300), añadir `@import '@fontsource/roboto/300.css';` en `styles.scss`.
- Los iconos (`Material Icons`) siguen cargándose desde Google Fonts en `index.html` (fuera del alcance de este cambio).

## Colores

| Token | Valor | Uso |
|---|---|---|
| `--nx-color-surface` | `#F6F7F9` | Fondo de página |
| `--nx-color-surface-raised` | `#FFFFFF` | Tarjetas, toolbar, menú lateral, menús, diálogos, grid |
| `--nx-color-surface-muted` | `#EEF0F3` | Zonas atenuadas, fondo del campo «fill», divisores de pestañas |
| `--nx-color-text` | `#1B1C1F` | Texto |
| `--nx-color-text-muted` | `#5A6068` | Texto atenuado (5.9:1 sobre el fondo, 6.3:1 sobre blanco) |
| `--nx-color-border` | `#DDE1E6` | Bordes decorativos (tarjetas, separadores) |
| `--nx-color-outline` | `#737880` | Bordes de controles (campo «outline», chips), ≥ 3:1 |
| `--nx-color-primary` | `#005CBB` | Primary (azul M3, ⚑ paleta corporativa abierta) |
| `--nx-color-primary-container` | `#D7E3FF` | Fondos seleccionados, evento «created» |
| `--nx-color-error` | `#BA1A1A` | Errores |

### Superficies de Material sobrescritas

La paleta azul de M3 genera superficies rosadas/lavanda. En `theme.scss`, `mat.theme-overrides(...)` (justo después de `mat.theme`) redirige estos tokens de sistema a los `--nx-*`:

| Token de sistema Material | → token NEXUS | Lo usan |
|---|---|---|
| `background` | `--nx-color-surface` | Fondo de `mat-sidenav-content` |
| `surface`, `surface-bright`, `surface-container-lowest/low/(base)/high` | `--nx-color-surface-raised` | Toolbar, `mat-sidenav`, tarjetas, menús, panel de `mat-select`, diálogos |
| `surface-container-highest`, `surface-variant`, `surface-dim` | `--nx-color-surface-muted` | Campo «fill», divisores de pestañas y menús |
| `on-surface`, `on-background` / `on-surface-variant` | `--nx-color-text` / `--nx-color-text-muted` | Textos, etiquetas, iconos |
| `outline` / `outline-variant` | `--nx-color-outline` / `--nx-color-border` | Bordes de controles / bordes decorativos |
| `primary`, `primary-container`, `error` | `--nx-color-primary`, `--nx-color-primary-container`, `--nx-color-error` | Botones, pestaña activa, foco, errores |

Los demás tokens `--mat-sys-*` (on-primary, secondary, tertiary, error-container, inverse-surface…) siguen saliendo de la paleta generadora.

## Colores de estado (`nx-status-badge`)

Activos desde 0.8 (decisión de Luciano, 2026-11-11). Para cambiarlos, editar `--nx-status-{success,warning,danger,neutral}-{bg,fg}` en `tokens.css`; el componente los aplica con `--mat-chip-elevated-container-color`, `--mat-chip-label-text-color` y `--mat-chip-outline-width: 0` (píldora rellena). El texto de aviso pasó de `#e65100` a `#8A3500` el 2026-11-12 (de 3.0:1 a 6.4:1 sobre `#ffe0b2`, WCAG AA). Contrastes actuales: éxito 5.9:1, aviso 6.4:1, peligro 4.7:1, neutro 7.6:1. La paleta corporativa sigue ⚑ abierta.

## Tipografía

| Nivel | Tokens | Valor |
|---|---|---|
| Título de página (`h1`) | `--nx-text-page-title` (+ `-size`, `-line`, `-weight`) | 28/36, 500 |
| Título de sección (`h2`) | `--nx-text-section-title` (+ `-size`, `-line`, `-weight`) | 18/24, 500 |
| Texto | `--nx-text-body` (+ `-size`, `-line`) | 14/20, 400 |
| Etiqueta / caption | `--nx-text-label` (+ `-size`, `-line`) | 12/16, 400 |
| Código | `--nx-text-mono-size` (con `--nx-font-mono`) | 13 |

Los tokens sin sufijo son atajos para la propiedad `font` (`font: var(--nx-text-section-title);`). `styles.scss` aplica `--nx-text-body` al `body`, `--nx-text-page-title` a `h1` y `--nx-text-section-title` a `h2`.

## Layout

| Token | Valor |
|---|---|
| `--nx-toolbar-height` | 56px |
| `--nx-sidenav-width` | 248px |
| `--nx-content-padding` | 32px (24px por debajo de 1280px, `@media` al final de `tokens.css`) |
| `--nx-page-max-width-form` | 1120px (ancho máximo de detalle y formulario) |
| `--nx-form-gap-x` / `--nx-form-gap-y` | 24px / 16px (rejilla de formulario) |

Radios `--nx-radius-sm|md|lg` = 4/8/12px. Espaciado `--nx-space-1..6` = 4/8/12/16/24/32px.

## Regla

Los componentes usan únicamente tokens `--nx-*`, nunca colores hexadecimales. Si falta un token, se añade a `tokens.css` con un comentario y se documenta aquí.

## Ejemplos

1. **Cambiar el fondo de página**: en `tokens.css`, `--nx-color-surface: #f2f3f5;`. Se propaga al `body` y al fondo de `mat-sidenav-content`. Para tarjetas, toolbar y menús, `--nx-color-surface-raised`.
2. **Cambiar el color primario**:
   - Color corporativo exacto: en `tokens.css`, `--nx-color-primary` (y `--nx-color-primary-container` para los fondos claros). Material lo recoge vía `theme-overrides`.
   - Paleta completa (tonos derivados como `on-primary`, `secondary`, estados): en `theme.scss`, cambiar `primary: mat.$blue-palette` por otra paleta (p. ej. `mat.$green-palette`) **y** actualizar los dos hex de `tokens.css` para que coincidan con su tono 40 y 90.
3. **Cambiar colores de evento o diff**: en `tokens.css`, p. ej. `--nx-event-created-bg: #e3f2fd; --nx-event-created-fg: #0d47a1;` o `--nx-diff-after-bg: #dcedc8;`.
4. **Más o menos redondeo / densidad**: en `tokens.css`, `--nx-radius-lg: 4px;` para esquinas casi rectas. Para tablas y controles más compactos, en `theme.scss` cambiar `density: 0` por `-1` o `-2`.
