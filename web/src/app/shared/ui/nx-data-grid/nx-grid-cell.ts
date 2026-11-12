import {
  ComponentRef,
  Directive,
  OnChanges,
  OnDestroy,
  Type,
  ViewContainerRef,
  inject,
  input,
  reflectComponentType,
} from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ColDef, ICellRendererParams, ValueFormatterParams, ValueGetterParams } from 'ag-grid-community';

/** Stable id of a column: `colId`, else `field`. */
export function columnId<T>(column: ColDef<T>): string | undefined {
  return column.colId ?? (column.field as string | undefined);
}

/** Cell value of `row` for `column` outside AG Grid (card view): `valueGetter` function or `field` path. */
export function cellValue<T>(column: ColDef<T>, row: T): unknown {
  if (typeof column.valueGetter === 'function') {
    return column.valueGetter({ data: row, colDef: column } as ValueGetterParams<T>);
  }
  if (!column.field) {
    return undefined;
  }
  return String(column.field)
    .split('.')
    .reduce<unknown>((value, key) => (value == null ? value : (value as Record<string, unknown>)[key]), row);
}

/** Display text of a cell outside AG Grid: `valueFormatter` function, else the value as text. */
export function cellText<T>(column: ColDef<T>, row: T): string {
  const value = cellValue(column, row);
  if (typeof column.valueFormatter === 'function') {
    return column.valueFormatter({ value, data: row, colDef: column } as ValueFormatterParams<T>) ?? '';
  }
  return value == null ? '' : String(value);
}

/** True when the column renders its cells with an Angular component (e.g. a status badge). */
export function hasComponentRenderer<T>(column: ColDef<T>): boolean {
  return typeof column.cellRenderer === 'function' && reflectComponentType(column.cellRenderer as Type<unknown>) !== null;
}

/** True when the column's `cellClass` asks for monospace text (codes). */
export function isMonoColumn<T>(column: ColDef<T>): boolean {
  const cellClass = column.cellClass;
  if (typeof cellClass === 'string') {
    return cellClass.split(/\s+/).includes('nx-mono');
  }
  return Array.isArray(cellClass) && cellClass.includes('nx-mono');
}

/**
 * Renders a column's AG Grid Angular cell renderer (`ICellRendererAngularComp`) outside the
 * grid, so the card view (< 600 px) shows the same status badge as the table.
 */
@Directive({
  selector: '[nxGridCell]',
  standalone: true,
})
export class NxGridCellDirective<T> implements OnChanges, OnDestroy {
  readonly nxGridCell = input.required<ColDef<T>>();
  readonly nxGridCellRow = input.required<T>();

  private readonly viewContainer = inject(ViewContainerRef);
  private componentRef: ComponentRef<ICellRendererAngularComp> | null = null;
  private rendererType: unknown = null;

  ngOnChanges(): void {
    const column = this.nxGridCell();
    const row = this.nxGridCellRow();
    const params = {
      value: cellValue(column, row),
      valueFormatted: null,
      data: row,
      colDef: column,
    } as unknown as ICellRendererParams<T>;
    if (this.componentRef && this.rendererType === column.cellRenderer && this.componentRef.instance.refresh(params)) {
      return;
    }
    this.viewContainer.clear();
    this.rendererType = column.cellRenderer;
    this.componentRef = this.viewContainer.createComponent(column.cellRenderer as Type<ICellRendererAngularComp>);
    this.componentRef.instance.agInit(params);
  }

  ngOnDestroy(): void {
    this.componentRef?.destroy();
  }
}
