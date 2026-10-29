import { Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';

/**
 * `nx-data-grid` status column cell renderer (`conventions-frontend.md §4`: "Columna de estado
 * con `nx-status-badge`"). Local to this feature (same approach as the Users feature's own
 * renderer) to avoid adding a new `shared/ui` component from this subtask.
 */
@Component({
  selector: 'nx-party-status-cell',
  standalone: true,
  imports: [NxStatusBadgeComponent],
  template: `<nx-status-badge [status]="status()" />`,
})
export class PartyStatusCellRendererComponent implements ICellRendererAngularComp {
  protected readonly status = signal<'active' | 'inactive'>('inactive');

  agInit(params: ICellRendererParams<unknown, boolean>): void {
    this.status.set(params.value ? 'active' : 'inactive');
  }

  refresh(params: ICellRendererParams<unknown, boolean>): boolean {
    this.status.set(params.value ? 'active' : 'inactive');
    return true;
  }
}
