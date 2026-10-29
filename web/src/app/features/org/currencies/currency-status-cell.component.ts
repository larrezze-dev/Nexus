import { Component, signal } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';

/**
 * AG Grid cell renderer wrapping `nx-status-badge` for the Currencies list `isActive` column
 * (`conventions-frontend.md §4`: "Columna de estado con `nx-status-badge`"). An Angular
 * component as `cellRenderer` is an AG Grid Community capability (not Enterprise-gated, unlike
 * the Server-Side Row Model `nx-data-grid` already avoids, see that component's docstring), so
 * this does not require touching `nx-data-grid` itself.
 */
@Component({
  selector: 'nx-currency-status-cell',
  imports: [NxStatusBadgeComponent],
  template: `<nx-status-badge [status]="status()" />`,
})
export class CurrencyStatusCellComponent implements ICellRendererAngularComp {
  protected readonly status = signal<'active' | 'inactive'>('inactive');

  agInit(params: ICellRendererParams<CurrencyListItem>): void {
    this.readRow(params);
  }

  refresh(params: ICellRendererParams<CurrencyListItem>): boolean {
    this.readRow(params);
    return true;
  }

  private readRow(params: ICellRendererParams<CurrencyListItem>): void {
    this.status.set(params.data?.isActive ? 'active' : 'inactive');
  }
}
