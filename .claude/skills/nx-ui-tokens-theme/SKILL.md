---
name: nx-ui-tokens-theme
description: Cómo controlar el aspecto de NEXUS web con tokens CSS --nx-*, components.css y el tema Material M3 (theme.scss), incluidos los overrides --mat-* de Material 22 y la carga de fuentes. Úsala al tocar colores, radios, espaciado, tipografía o estilos de componentes Material.
---

# Tokens y tema

Fuente de verdad: `web/src/styles/README.md`. Ficheros:
- `theme.scss`: tema Material M3 (paleta primary/tertiary, tipografía, densidad).
- `tokens.css`: tokens semánticos `--nx-*` (superficies, texto, estados, eventos, diff, radios, espaciado, fuentes, layout).
- `components.css`: clases genéricas `.nx-*` que solo usan tokens.

## Reglas

1. Los componentes usan **solo** `var(--nx-*)`. Nunca hex, `rgb()`, px sueltos de color ni `color="primary"` a ciegas. Si falta un token, se añade en `tokens.css` con un comentario de una línea y se documenta en el README.
2. Espaciado: solo `--nx-space-1..6` (4/8/12/16/24/32). Radios: `--nx-radius-sm|md|lg`. Ningún `margin: 13px`.
3. Nada de estilos de componente que redefinan Material desde un `styles:` local: si un ajuste se repite en dos pantallas, va a `components.css` o a un componente compartido.
4. La paleta corporativa sigue ⚑ abierta: no la inventes. Los cambios de color pasan por Luciano (regla dura de UI, CLAUDE.md §11).

## Material 22 (M3)

- Material 22 **ignora** los `--mdc-*` antiguos. Se personaliza con `--mat-<componente>-<propiedad>` (p. ej. `--mat-chip-elevated-container-color`, `--mat-chip-label-text-color`, `--mat-chip-outline-width`), o con los mixins `mat.<componente>-overrides(...)` en `theme.scss`. Lección de 0.8: el chip de estado no cambiaba de color hasta usar `--mat-chip-*`.
- Busca el nombre exacto del token en `node_modules/@angular/material/<componente>/_m3-<componente>.scss` (o el `_<componente>-theme.scss`); no lo adivines.
- `typography` del tema define `--mat-sys-*-font`. Si la familia no tiene fallback, Material cae a la serif del navegador. La familia la fija `--nx-font-family`, y tema y tokens deben apuntar a la misma pila.

## Fuentes

- Roboto autoalojada con `@fontsource/roboto` (400/500/700) y `@fontsource/roboto-mono` (400), aprobados en ADR-0010 e importados en `angular.json`/`styles.scss` (no desde Google Fonts). Antes de 2026-11-12 no se cargaba y Material caía a Times (auditoría T1).
- Comprueba siempre con `report.json` de `shots.mjs` que ninguna fuente calculada cae en serif.

## Checklist

- [ ] `grep -nE '#[0-9a-fA-F]{3,8}\b|rgb\(' <ficheros tocados>` vacío fuera de `tokens.css`.
- [ ] Ningún token nuevo sin documentar en el README.
- [ ] Capturas de/es revisadas tras cualquier cambio de tema (afecta a toda la app: revisa también pantallas que no tocaste).
