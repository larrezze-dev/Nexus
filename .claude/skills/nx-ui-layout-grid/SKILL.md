---
name: nx-ui-layout-grid
description: Rejilla, anchos de página, breakpoints, escala de espaciado y comportamiento del shell (sidenav, toolbar) en NEXUS web. Úsala al maquetar cualquier página o formulario.
---

# Layout y rejilla

> Valores aprobados por Luciano el 2026-11-12 (`conventions-frontend.md §4.2–§4.3`, canvas tablero 1). Tokens en `web/src/styles/tokens.css` (los de layout —`--nx-toolbar-height`, `--nx-sidenav-width` 248, `--nx-content-padding`, `--nx-page-max-width-form`, `--nx-form-gap-x/y`— se crean o ajustan en la subtarea F1 de 2026-11-12; si no existen todavía, créalos con estos valores).

## Principios

1. **Un único borde izquierdo**: migas, título, filtros, grid, pestañas y formulario empiezan en la misma x.
2. **Ancho de página por tipo**: las listas usan todo el ancho disponible; detalle y formulario, máximo 1120 px (`--nx-page-max-width-form`). Barra superior 56 px (`--nx-toolbar-height`), menú lateral 248 px (`--nx-sidenav-width`).
3. **Rejilla de formulario de 12 columnas** (`nx-form-grid`), separación 24 px horizontal (`--nx-form-gap-x`) y 16 px vertical (`--nx-form-gap-y`); CSS grid, no anchos fijos en px. Cada campo declara su tramo: S = 3, M = 4, L = 6, XL = 12. Nunca `width: 212px`.
4. **Secciones**: los campos se agrupan en secciones con título (`.nx-form-section`); un formulario largo no es una lista plana de campos.
5. **Escala de espaciado** `--nx-space-1..6` y nada fuera de ella. Entre secciones `--nx-space-5`, entre campos `--nx-space-4`, de la cabecera al contenido `--nx-space-5`.
6. **Alturas de fila**: `mat-form-field appearance="outline"` en toda la app; nunca etiquetas fuera del campo.

## Breakpoints

| Nombre | Ancho | Comportamiento |
|---|---|---|
| compact | < 600 px | sidenav `over` cerrado (☰), formulario en 1 columna, lista en tarjetas, acciones en barra inferior fija |
| medium | 600–1279 px | sidenav `over` cerrado por defecto (☰ lo abre encima), formulario a 6 columnas, margen 24 px |
| expanded | ≥ 1280 px | sidenav `side` abierto por defecto; ☰ lo oculta del todo y la elección se recuerda (localStorage `nx.sidenav`), formulario a 12 columnas, margen 32 px |

Usa `BreakpointObserver` del CDK para el sidenav. Para el contenido, CSS (`@media` o container queries), no lógica en TS.

## Grid de datos

- `nx-data-grid` ocupa la altura disponible de la página (sin altura fija de 240 px). Con paginación de servidor, la página entera de filas debe ser visible o el grid debe hacer scroll **dentro** de sí mismo con la cabecera fija; nunca filas cortadas en la última línea visible.
- Las columnas flexibles llenan todo el ancho (sin hueco tras la última columna). La columna de estado es estrecha y fija.

## Comprobación

Capturas a 1440 y 390 (`nx-ui-visual-review`). A 390: sin overflow horizontal y con el menú cerrado por defecto.
