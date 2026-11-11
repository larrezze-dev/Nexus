# Estilos de NEXUS web

Todo el aspecto de la interfaz se adapta editando solo CSS.

| Fichero | Qué controla |
|---|---|
| `theme.scss` | Tema Angular Material M3: paleta (primary/tertiary), tipografía y densidad. |
| `tokens.css` | Tokens semánticos NEXUS (`--nx-*`): colores de superficie y texto, estados (ver aviso abajo), tipos de evento, diff, radios, espaciado, fuentes y layout. |
| `components.css` | Clases genéricas (`.nx-header-bar`, `.nx-card`, `.nx-empty-state`, `.nx-alert`, `.nx-chip-outline`, `.nx-mono`) que solo usan tokens. |

`styles.scss` solo importa el tema y fija la fuente del `body`. Ambos CSS se cargan desde `angular.json`.

## Aviso: colores de `nx-status-badge` aún no activos

Angular Material 22 ya no lee `--mdc-chip-*`; solo `--mat-chip-elevated-container-color` y `--mat-chip-label-text-color`. Por eso `nx-status-badge` hoy no aplica los colores y editar `--nx-status-*` no cambia nada en esos chips (sí en `--nx-diff-after-*`). Para activarlos (cambio visual, pendiente de decisión de Luciano), descomentar las líneas `--mat-chip-*` en `nx-status-badge.component.scss`.

## Regla

Los componentes usan únicamente tokens `--nx-*`, nunca colores hexadecimales. Si falta un token, se añade a `tokens.css`.

## Ejemplos

1. **Cambiar el color primario**: en `theme.scss`, cambiar `primary: mat.$blue-palette` por otra paleta (p. ej. `mat.$green-palette`). `--nx-color-primary` y los eventos "created" lo heredan. Para un color corporativo exacto, sobrescribir `--nx-color-primary` en `tokens.css`.
2. **Cambiar colores de evento o diff**: en `tokens.css`, p. ej. `--nx-event-created-bg: #e3f2fd; --nx-event-created-fg: #0d47a1;` o `--nx-diff-after-bg: #dcedc8;`.
3. **Más o menos redondeo / densidad**: en `tokens.css`, `--nx-radius-lg: 4px;` para esquinas casi rectas. Para tablas y controles más compactos, en `theme.scss` cambiar `density: 0` por `-1` o `-2`.

Otros ajustes habituales: ancho del menú lateral (`--nx-sidenav-width`), relleno del contenido (`--nx-content-padding`), fuente (`--nx-font-family`).
