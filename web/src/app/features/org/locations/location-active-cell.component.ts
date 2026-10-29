import { Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';

/**
 * `nx-data-grid` cell renderer that shows the `isActive` column with `nx-status-badge`
 * (`conventions-frontend.md §4`: the list screen reuses the shared status badge), instead
 * of a plain boolean/text cell.
 */
@Component({
  selector: 'nx-location-active-cell',
  standalone: true,
  imports: [NxStatusBadgeComponent],
  template: `<nx-status-badge [status]="status()" />`,
})
export class LocationActiveCellComponent implements ICellRendererAngularComp {
  protected readonly status = signal<'active' | 'inactive'>('inactive');

  agInit(params: ICellRendererParams<unknown, boolean>): void {
    this.status.set(params.value ? 'active' : 'inactive');
  }

  refresh(params: ICellRendererParams<unknown, boolean>): boolean {
    this.status.set(params.value ? 'active' : 'inactive');
    return true;
  }
}
