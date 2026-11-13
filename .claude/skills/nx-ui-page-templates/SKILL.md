---
name: nx-ui-page-templates
description: Plantillas de página de NEXUS web (lista, detalle, formulario de alta, estados) con su jerarquía de componentes Angular Material M3 y los componentes de layout compartidos. Úsala al crear o rehacer cualquier pantalla.
---

# Plantillas de página

Fuente normativa: `docs/00-foundation/conventions-frontend.md §4` (sistema de diseño aprobado por Luciano el 2026-11-12). Referencia visual: canvas https://claude.ai/artifact/8giDWSAii8cRFoTQi7qDjH (tableros 2 lista, 3 detalle, 4 formulario, 5 estados, 7 móvil). Si una pantalla no encaja en estas plantillas, **PARA** y avisa al orquestador: no inventes una variante.

## Componentes de layout (`web/src/app/shared/ui/layout/`)

| Componente | API | Para qué |
|---|---|---|
| `nx-page` | `width: 'full' \| 'form'` | Contenedor: ancho completo (listas) o máx. `--nx-page-max-width-form` (detalle/formulario); gap vertical `--nx-space-5` |
| `nx-page-header` | `title`, `subtitle?`, `count?` + slots `[nxHeaderStatus]`, `[nxHeaderActions]` | Migas (las pinta él), título h1, chip, subtítulo (código · fecha), acciones a la derecha, todo en una fila |
| `nx-form-section` | `title`, `hint?` | Tarjeta blanca con título h2 y ayuda |
| `nx-form-grid` | hijos con atributo `nxSpan="s\|m\|l\|xl"` | Rejilla de 12 col (6 < 1280, 1 < 600) |
| `nx-action-bar` | contenido proyectado | Barra al pie, alineada a la derecha, borde superior; fija abajo < 600 px |
| `nx-filter-bar` | contenido + `(clear)` | Fila de filtros dentro de la tarjeta de lista |
| `nx-empty-state` | `kind: 'empty'\|'no-results'\|'error'\|'forbidden'\|'not-found'`, `title`, `message`, slot de acción | Estados de §4.7 |
| `nx-section-index` | `sections: {id, label}[]` | Índice fijo a la derecha en formularios de más de 4 secciones (≥ 1280 px) |
| `NxConfirm` (servicio) | `confirm({title, message, confirmLabel, destructive}) → Promise<boolean>` | Diálogo de confirmación (nunca `confirm()` del navegador) |

## Lista

```html
<nx-page width="full">
  <nx-page-header [title]="'org.legalEntities.title' | transloco" [count]="total()">
    <a nxHeaderActions mat-flat-button routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
      <mat-icon>add</mat-icon>{{ 'org.legalEntities.actions.create' | transloco }}
    </a>
  </nx-page-header>
  <section class="nx-card nx-card--flush">  <!-- .nx-card--flush: tarjeta sin padding (se crea en components.css con la base de layout) -->
    <nx-filter-bar (clear)="clearFilters()">
      <mat-form-field appearance="outline" class="nx-filter--search">
        <mat-label>{{ 'org.legalEntities.filters.q' | transloco }}</mat-label>
        <mat-icon matPrefix>search</mat-icon>
        <input matInput [formControl]="q" />
      </mat-form-field>
      <mat-form-field appearance="outline"> <mat-label>…Estado</mat-label> <mat-select …/> </mat-form-field>
    </nx-filter-bar>
    <nx-data-grid [columns]="columns()" [fetch]="fetch" [filters]="filters()" (rowOpen)="open($event)" />
  </section>
</nx-page>
```
- Columnas en un `computed()` que depende del idioma (skill `nx-ui-i18n`). Código primero (mono), nombre después, estado al final (estrecha).
- Solo los filtros que ofrece el contrato. Etiqueta siempre dentro del campo (`appearance="outline"` en filtros).

## Detalle

```html
<nx-page width="form">
  <nx-page-header [title]="name()" [subtitle]="code() + ' · ' + modifiedLabel()">
    <nx-status-badge nxHeaderStatus [status]="status()" />
    <button nxHeaderActions mat-stroked-button class="nx-btn--danger" (click)="deactivate()">…Desactivar</button>
  </nx-page-header>
  <mat-tab-group mat-stretch-tabs="false" animationDuration="0">
    <mat-tab [label]="'common.tabs.general' | transloco">
      <form [formGroup]="form">
        <nx-form-section [title]="'…identification' | transloco">
          <nx-form-grid>
            <mat-form-field nxSpan="s"> <mat-label>Código</mat-label> <input matInput formControlName="code" /> </mat-form-field>
          </nx-form-grid>
        </nx-form-section>
        <nx-action-bar>
          <button mat-button type="button" (click)="cancel()">Cancelar</button>
          <button mat-flat-button type="submit" [disabled]="form.pristine || readOnly()">Guardar cambios</button>
        </nx-action-bar>
      </form>
    </mat-tab>
    <mat-tab *nxCan="'AUDIT:READ'" [label]="'common.tabs.audit' | transloco"> <nx-audit-trail … /> </mat-tab>
  </mat-tab-group>
</nx-page>
```
- `.nx-btn--danger` (components.css): contorno y texto `--nx-color-error`, para Desactivar.
- Título = nombre del registro (caption con `nxCaption`, razón social…); el código va en el subtítulo, en mono. Llamar a `NxBreadcrumbService.setCurrentLabel(name)` al cargar.
- Campos de formulario: `appearance="outline"` (por defecto global), etiqueta dentro del campo, `mat-hint` debajo.
- Selector de entidad: muestra «código + nombre» del valor dentro del campo, con botón de quitar si es opcional. Nada de «Actual: …» encima.
- Campos inmutables: solo lectura con icono de candado (`matSuffix`) y `mat-hint` que explica por qué.
- Solo lectura (SYSTEM, sin permiso): `form.disable()`, Guardar deshabilitado y aviso `nx-alert` arriba.
- Desactivar: `NxConfirm.confirm({destructive: true, …})` con el nombre del registro en el mensaje; aviso breve (snackbar) tras la acción.
- 409: `nx-alert` de aviso «Otro usuario ha modificado…» con botón «Recargar».

## Formulario de alta

Igual que el detalle sin pestañas: `nx-page-header` con título «Nuevo X» y una frase explicativa (`subtitle`), secciones, `nx-action-bar` con «Cancelar · Crear X». Errores al enviar: `nx-alert--error` arriba con el número de campos a revisar (`role="alert"`), `markAllAsTouched()` y foco en el primer campo inválido. Si hay más de 4 secciones, índice lateral (`nx-section-index`) fijo a la derecha a partir de 1280 px.

## Estados

`nx-data-grid` muestra esqueleto, vacío, sin resultados y error con `nx-empty-state`. Las páginas de detalle usan `nx-empty-state kind="not-found"` si el GET devuelve 404, y `kind="error"` con «Reintentar» si falla. Las rutas `/forbidden`, `/not-found` y `**` están dentro del shell; `/unauthorized` está fuera.

## Móvil (< 600 px)

La lista pasa a tarjetas (nombre, código, estado, chevron) y «Nuevo» es un FAB extendido. El detalle tiene la barra de acciones fija abajo y los idiomas fr/it/es del caption plegados bajo «Mostrar más idiomas».

## Checklist de plantilla

- [ ] Usa `nx-page` + `nx-page-header`; no hay cabeceras hechas a mano ni `.nx-page-header` locales.
- [ ] Un único botón `mat-flat-button` por vista.
- [ ] Todos los campos en `nx-form-grid` con `nxSpan`; ningún `width:` en px.
- [ ] Pestañas sin estirar, en el orden General · … · Auditoría.
- [ ] Migas con el nombre del registro; título de pestaña del navegador correcto.
- [ ] Estados vistos en capturas (vacío, error, 404, solo lectura).
