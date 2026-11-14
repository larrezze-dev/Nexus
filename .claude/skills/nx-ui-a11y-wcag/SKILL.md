---
name: nx-ui-a11y-wcag
description: Checklist WCAG 2.1 AA para NEXUS web (contraste, foco, etiquetas, teclado, ARIA de pestañas/grid/diálogos) y pasada axe-core con Playwright desde scratch. Úsala al terminar cualquier componente o pantalla, y al revisar UI.
---

# Accesibilidad (WCAG 2.1 AA)

Obligatoria en componentes compartidos (`conventions-frontend.md §8`) y exigida también en las pantallas que maquetes.

## Checklist

- [ ] **Contraste**: texto normal ≥ 4.5:1, texto grande y bordes de controles ≥ 3:1. Comprueba los chips de estado (`--nx-status-*`) y los textos atenuados (`--nx-color-text-muted`) sobre su fondo real.
- [ ] **Color no es el único canal**: el estado lleva texto («Activo»), no solo un color; el diff antes/después lleva tachado o etiqueta además del color.
- [ ] **Foco visible** en todo lo interactivo, sin quitar el `outline` de Material; el orden de tabulación sigue el visual.
- [ ] **Teclado**: todo se puede usar sin ratón (filas del grid abren el detalle con Enter, pestañas con flechas, menús con Esc). Nada de `div (click)` sin `button` o `role` + `tabindex` + teclas.
- [ ] **Etiquetas**: cada campo tiene `mat-label` (no solo placeholder); los botones de icono tienen `aria-label` traducido; los iconos decorativos llevan `aria-hidden="true"`.
- [ ] **Estructura**: un `h1` por página (el título), jerarquía `h2/h3` sin saltos, `nav` con `aria-label` para migas y menú, `main` en el contenido.
- [ ] **Mensajes**: errores de formulario asociados al campo (`mat-error`); avisos y errores globales con `role="alert"` o `aria-live`.
- [ ] **Idioma**: `<html lang>` = idioma activo.
- [ ] **Zoom 200 %** y 390 px sin pérdida de contenido ni scroll horizontal.

## axe-core (sin tocar el repo)

```bash
cd <scratch>/pw && npm i playwright@1.65.0 @axe-core/playwright@4.10.2
```
```js
const { AxeBuilder } = require('@axe-core/playwright');
const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
console.log(r.violations.map(v => `${v.id} (${v.impact}) ×${v.nodes.length}`));
```
Ejecútalo tras el login (reutiliza el flujo de `shots.mjs`) en cada ruta tocada. Objetivo: 0 violaciones `serious`/`critical`. Las de AG Grid que no se pueden corregir desde fuera se listan en la entrega con su motivo.

## Entrega

Checklist marcada y salida de axe por ruta (id, impacto, número de nodos).
