---
name: nx-ui-i18n
description: Patrones i18n de NEXUS web con Transloco (de/en/fr/it/es) — cabeceras de grid reactivas, paginador, fechas, AG Grid, enums, caption_i18n, nombres de claves. Úsala siempre que añadas o toques texto visible.
---

# i18n en NEXUS web

Idiomas: `de` (obligatorio en datos), `en`, `fr`, `it`, `es`. Ficheros: `web/public/assets/i18n/<lang>.json`. El idioma activo lo fija `NxI18nService` (`core/i18n/nx-i18n.service.ts`) a partir de `/me.preferredLanguage`, y el usuario lo cambia en caliente.

## Reglas

1. **Ningún texto fijo** en plantillas ni en TS: todo pasa por una clave. Incluye `aria-label`, `title`, placeholders, tooltips, mensajes de snackbar y textos de diálogo.
2. **Las 5 lenguas a la vez**: una clave nueva se añade a los 5 ficheros en el mismo commit. Si no conoces la traducción buena en fr/it, escribe una razonable y márcala en la entrega; nunca dejes la clave sin entrada.
3. **Claves** `feature.page.element` en inglés, camelCase: `org.locations.list.columns.code`. Grupos permitidos: `common`, `shared`, `nav`, `shell`, `breadcrumb`, `errors`, `httpPages`, `account`, `org`, `access`, `audit`, `events`, `pageTitle`. `locations.*` y `warehouses.*` se migran a `org.*` (renombrar en los 5 ficheros y en los tests). Pestañas: `common.tabs.general`, `common.tabs.audit`.
4. **Vocabulario de usuario final** (ver glosario): «Entidad legal», no «Legal Entity»; «Almacén», no «Warehouse»; en `de` términos Nüssli («Gesellschaft», «Lager»). Nada de jerga técnica (UUID, oid, scope, true, «paso 2.1»).

## Patrones obligatorios

- **Cabeceras de AG Grid**: nunca `headerName: this.transloco.translate(...)` en un campo inicializado al construir: corre antes de que cargue el JSON y deja la clave cruda para siempre. Usa `headerValueGetter`, o un `computed()` de columnas que dependa de una señal de idioma/traducciones cargadas (`toSignal(transloco.selectTranslation())` o `langChanges$`) y vuelve a pasar `columnDefs` al grid. `nx-data-grid` debe aceptar columnas reactivas.
- **Paginador**: `MatPaginatorIntl` propio que lee de Transloco y llama a `changes.next()` al cambiar de idioma («Elemente pro Seite», «1–4 von 4»).
- **AG Grid**: `localeText` por idioma (overlay de carga y de «sin filas», menús). No dejes el «Loading...» por defecto.
- **Fechas y números**: `DateAdapter.setLocale(lang)` al cambiar de idioma (`de-CH`, `es-ES`, …). Visualización en `Europe/Zurich`. Nunca `<input type="date">` nativo (sale `mm/dd/yyyy`): usa `mat-datepicker`. Importes y cantidades con `Intl.NumberFormat` del idioma activo.
- **Enums**: cada valor tiene su clave (`org.parties.type.PERSON`); nunca se muestra el literal del API.
- **Datos traducibles (`caption_i18n`)**: idioma activo → `de` → `en` → primero disponible, siempre con el pipe `nxCaption` o `resolveCaption` de `shared/ui/caption/`; nunca `.de` ni resolución a mano.
- **`<html lang>` y `document.title`**: se actualizan con el idioma activo y la página (`TitleStrategy` con clave i18n).

## Comprobación

- `shots.mjs` (skill `nx-ui-visual-review`) en `de` y `es`: `rawKeys` y `rawKeysFirstLoad` vacíos en `report.json`.
- Un test de componente por patrón (cabeceras cambian al cambiar de idioma, paginador traducido).
- Script rápido de completitud: las claves de los 5 JSON son el mismo conjunto (`jq -r 'paths(scalars)|join(".")'` y `diff`).
