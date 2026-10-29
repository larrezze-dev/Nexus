import { Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';

/**
 * `nx-data-grid` status column cell renderer (`conventions-frontend.md §4`: "Columna de estado
 * con `nx-status-badge`"). AG Grid instantiates this directly as an Angular component renderer
 * (`ag-grid-angular` 36 framework support), so it only needs to implement
 * `ICellRendererAngularComp`'s two lifecycle hooks.
 */
@Component({
  selector: 'nx-user-status-cell',
  standalone: true,
  imports: [NxStatusBadgeComponent],
  template: `<nx-status-badge [status]="status()" />`,
})
export class UserStatusCellRendererComponent implements ICellRendererAngularComp {
  protected readonly status = signal<'active' | 'inactive'>('inactive');

  agInit(params: ICellRendererParams<unknown, boolean>): void {
    this.status.set(params.value ? 'active' : 'inactive');
  }

  refresh(params: ICellRendererParams<unknown, boolean>): boolean {
    this.status.set(params.value ? 'active' : 'inactive');
    return true;
  }
}
