import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { Observable } from 'rxjs';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { LocationActiveCellComponent } from './location-active-cell.component';
import { LocationFacade } from './location.facade';

/**
 * Location list screen (ficha 0.4, track F): filters + `nx-data-grid` + `nx-status-badge`
 * (`conventions-frontend.md §4`). `GET /v1/locations` has no filters in the 0.4 contract,
 * so there is no filter row here — only pagination/sort, driven by `nx-data-grid` itself.
 */
@Component({
  selector: 'nx-location-list-page',
  imports: [NxDataGridComponent, MatButtonModule, MatIconModule, RouterLink, TranslocoModule],
  template: `
    <div class="nx-page-header">
      <h1>{{ 'locations.title' | transloco }}</h1>
      <button mat-raised-button color="primary" routerLink="/locations/new" data-testid="location-create-button">
        <mat-icon>add</mat-icon>
        {{ 'locations.actions.create' | transloco }}
      </button>
    </div>

    <nx-data-grid [columns]="columns" [fetch]="fetch" (selectionChanged)="onSelectionChanged($event)" />
  `,
  styles: [
    `
      .nx-page-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 1rem;
      }
    `,
  ],
})
export class LocationListPage {
  private readonly facade = inject(LocationFacade);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly columns: ColDef<LocationListItem>[] = [
    { field: 'code', headerName: this.transloco.translate('locations.list.columns.code'), flex: 1 },
    { field: 'name', headerName: this.transloco.translate('locations.list.columns.name'), flex: 2 },
    { field: 'city', headerName: this.transloco.translate('locations.list.columns.city'), flex: 1 },
    { field: 'countryCode', headerName: this.transloco.translate('locations.list.columns.countryCode'), flex: 1 },
    {
      field: 'isActive',
      headerName: this.transloco.translate('locations.list.columns.isActive'),
      flex: 1,
      cellRenderer: LocationActiveCellComponent,
    },
  ];

  /** `nx-data-grid`'s `fetch` input; `Location` has no list filters, so the 4th arg is ignored. */
  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Observable<Page<LocationListItem>> => this.facade.list(page, pageSize, sort);

  /** No per-row edit affordance on the shared grid: a single-row selection (click) opens the detail page. */
  protected onSelectionChanged(rows: LocationListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigateByUrl(`/locations/${rows[0].id}`);
    }
  }
}
