import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { Observable, debounceTime } from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { WarehouseActiveCellComponent } from './warehouse-active-cell.component';
import { WarehouseFacade, WarehouseListFilters } from './warehouse.facade';

type IsActiveFilterOption = 'all' | 'active' | 'inactive';

/**
 * Warehouse list screen (ficha 0.4, track F): filters (`legalEntityId`, `businessUnitId`,
 * `isActive`, `q` — `domain/org.md §5`) + `nx-data-grid` + `nx-status-badge`
 * (`conventions-frontend.md §4`).
 */
@Component({
  selector: 'nx-warehouse-list-page',
  imports: [
    NxDataGridComponent,
    NxEntityPickerComponent,
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    RouterLink,
    TranslocoModule,
  ],
  template: `
    <div class="nx-page-header">
      <h1>{{ 'warehouses.title' | transloco }}</h1>
      <button mat-raised-button color="primary" routerLink="/warehouses/new" data-testid="warehouse-create-button">
        <mat-icon>add</mat-icon>
        {{ 'warehouses.actions.create' | transloco }}
      </button>
    </div>

    <div class="nx-filter-row">
      <mat-form-field>
        <mat-label>{{ 'warehouses.filters.search' | transloco }}</mat-label>
        <input matInput [formControl]="qControl" data-testid="warehouse-filter-q" />
      </mat-form-field>

      <div class="nx-filter-entity">
        <span class="nx-filter-entity__label">{{ 'warehouses.filters.legalEntity' | transloco }}</span>
        @if (legalEntityLabel(); as label) {
          <span class="nx-filter-entity__value">{{ label }}</span>
          <button mat-icon-button type="button" (click)="clearLegalEntityFilter()" [attr.aria-label]="'common.cancel' | transloco">
            <mat-icon>close</mat-icon>
          </button>
        } @else {
          <nx-entity-picker [search]="searchLegalEntities" [displayWith]="displayCode" (selected)="onLegalEntitySelected($event)" />
        }
      </div>

      <div class="nx-filter-entity">
        <span class="nx-filter-entity__label">{{ 'warehouses.filters.businessUnit' | transloco }}</span>
        @if (businessUnitLabel(); as label) {
          <span class="nx-filter-entity__value">{{ label }}</span>
          <button mat-icon-button type="button" (click)="clearBusinessUnitFilter()" [attr.aria-label]="'common.cancel' | transloco">
            <mat-icon>close</mat-icon>
          </button>
        } @else {
          <nx-entity-picker [search]="searchBusinessUnits" [displayWith]="displayCode" (selected)="onBusinessUnitSelected($event)" />
        }
      </div>

      <mat-form-field>
        <mat-label>{{ 'warehouses.filters.isActive' | transloco }}</mat-label>
        <mat-select [value]="isActiveFilter()" (selectionChange)="isActiveFilter.set($event.value)" data-testid="warehouse-filter-is-active">
          <mat-option value="all">{{ 'warehouses.filters.isActiveAll' | transloco }}</mat-option>
          <mat-option value="active">{{ 'shared.status.active' | transloco }}</mat-option>
          <mat-option value="inactive">{{ 'shared.status.inactive' | transloco }}</mat-option>
        </mat-select>
      </mat-form-field>
    </div>

    <nx-data-grid [columns]="columns" [fetch]="fetch" [filters]="filters()" (selectionChanged)="onSelectionChanged($event)" />
  `,
  styles: [
    `
      .nx-page-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 1rem;
      }
      .nx-filter-row {
        display: flex;
        flex-wrap: wrap;
        gap: 1rem;
        align-items: center;
        margin-bottom: 1rem;
      }
      .nx-filter-entity {
        display: flex;
        align-items: center;
        gap: 0.25rem;
      }
    `,
  ],
})
export class WarehouseListPage {
  private readonly facade = inject(WarehouseFacade);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly legalEntityIdFilter = signal<string | null>(null);
  protected readonly legalEntityLabel = signal<string | null>(null);
  protected readonly businessUnitIdFilter = signal<string | null>(null);
  protected readonly businessUnitLabel = signal<string | null>(null);
  protected readonly isActiveFilter = signal<IsActiveFilterOption>('all');

  protected readonly qControl = new FormControl('', { nonNullable: true });
  private readonly qFilter = toSignal(this.qControl.valueChanges.pipe(debounceTime(300)), { initialValue: '' });

  /**
   * Typed as `Record<string, unknown>`, not `WarehouseListFilters`, to match `nx-data-grid`'s
   * `[filters]` input (`page.model.ts`); `fetch` below re-narrows it back to
   * `WarehouseListFilters` before calling the facade.
   */
  protected readonly filters = computed<Record<string, unknown>>(() => ({
    legalEntityId: this.legalEntityIdFilter() ?? undefined,
    businessUnitId: this.businessUnitIdFilter() ?? undefined,
    isActive: this.isActiveFilter() === 'all' ? undefined : this.isActiveFilter() === 'active',
    q: this.qFilter() || undefined,
  }));

  protected readonly columns: ColDef<WarehouseListItem>[] = [
    { field: 'code', headerName: this.transloco.translate('warehouses.list.columns.code'), flex: 1 },
    {
      headerName: this.transloco.translate('warehouses.list.columns.caption'),
      flex: 2,
      valueGetter: (params) => {
        const captionI18n = params.data?.captionI18n;
        const lang = this.transloco.getActiveLang();
        return (captionI18n && (captionI18n as unknown as Record<string, string>)[lang]) || captionI18n?.de || '';
      },
    },
    {
      field: 'isActive',
      headerName: this.transloco.translate('warehouses.list.columns.isActive'),
      flex: 1,
      cellRenderer: WarehouseActiveCellComponent,
    },
  ];

  protected readonly searchLegalEntities = this.facade.searchLegalEntities;
  protected readonly searchBusinessUnits = this.facade.searchBusinessUnits(null);

  protected readonly displayCode = (item: { code: string }): string => item.code;

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: Record<string, unknown>,
  ): Observable<Page<WarehouseListItem>> => this.facade.list(page, pageSize, sort, filters as WarehouseListFilters);

  protected onLegalEntitySelected(item: LegalEntityListItem): void {
    this.legalEntityIdFilter.set(item.id);
    this.legalEntityLabel.set(item.code);
  }

  protected clearLegalEntityFilter(): void {
    this.legalEntityIdFilter.set(null);
    this.legalEntityLabel.set(null);
  }

  protected onBusinessUnitSelected(item: BusinessUnitListItem): void {
    this.businessUnitIdFilter.set(item.id);
    this.businessUnitLabel.set(item.code);
  }

  protected clearBusinessUnitFilter(): void {
    this.businessUnitIdFilter.set(null);
    this.businessUnitLabel.set(null);
  }

  /** No per-row edit affordance on the shared grid: a single-row selection (click) opens the detail page. */
  protected onSelectionChanged(rows: WarehouseListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigateByUrl(`/warehouses/${rows[0].id}`);
    }
  }
}
