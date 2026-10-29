import { Component } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';

/**
 * AG Grid cell renderer mapping an `isActive: boolean` cell value to
 * `nx-status-badge`'s `active`/`inactive` status (`conventions-frontend.md
 * §4`: "Columna de estado con `nx-status-badge`"). `nx-data-grid`'s
 * `columns` input is a plain AG Grid `ColDef`, which only accepts an Angular
 * *component class* as `cellRenderer` (via `ICellRendererAngularComp`'s
 * `agInit`/`refresh`) — not a template string — so this tiny wrapper is how
 * `LegalEntity`/`BusinessUnit` list pages plug the shared badge into a grid
 * column.
 */
@Component({
  selector: 'nx-org-active-status-cell',
  standalone: true,
  imports: [NxStatusBadgeComponent],
  template: `<nx-status-badge [status]="status" />`,
})
export class OrgActiveStatusCellComponent<T> implements ICellRendererAngularComp {
  protected status: 'active' | 'inactive' = 'inactive';

  agInit(params: ICellRendererParams<T, boolean>): void {
    this.status = params.value ? 'active' : 'inactive';
  }

  refresh(params: ICellRendererParams<T, boolean>): boolean {
    this.status = params.value ? 'active' : 'inactive';
    return true;
  }
}
