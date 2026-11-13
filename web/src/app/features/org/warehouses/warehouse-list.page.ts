import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, catchError, debounceTime, map, of, tap } from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import {
  NxFilterBarComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { OrgNameCellComponent } from '../shared/org-name-cell.component';
import { WarehouseFacade, WarehouseListFilters } from './warehouse.facade';

/** Status filter: all, only active, only inactive (`isActive` query parameter). */
export type WarehouseStatusFilter = 'all' | 'active' | 'inactive';

/** Debounce of the search box before the list reloads. */
export const WAREHOUSE_SEARCH_DEBOUNCE_MS = 300;

/** «No filter» value of the legal-entity and business-unit selects. */
const ALL = '';

/**
 * Warehouse list (`conventions-frontend.md §4.4`, list template): header with the record count
 * and «New warehouse», a filter bar with the four filters `GET /v1/warehouses` offers (search on
 * code and name, legal entity, business unit, status) and the server-paginated `nx-data-grid`.
 *
 * The list items only carry the legal-entity and business-unit ids, so the page reads both
 * reference lists once (they also feed the two filter selects) and shows them by name, with the
 * code on a second line. The business-unit filter only offers the units of the chosen legal
 * entity. The API sorts by code and status. A row (click, Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-warehouse-list-page',
  standalone: true,
  imports: [
    RouterLink,
    ReactiveFormsModule,
    TranslocoModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    NxCanDirective,
    NxDataGridComponent,
    NxFilterBarComponent,
    NxPageComponent,
    NxPageHeaderComponent,
  ],
  template: `
    <nx-page width="full">
      <nx-page-header
        [breadcrumb]="true"
        [title]="'org.warehouses.title' | transloco"
        [count]="total()"
      >
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="warehouse-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.warehouses.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.warehouses.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-filter-bar [showClear]="hasFilters()" (clear)="clearFilters()">
          <mat-form-field class="nx-filter--search">
            <mat-label>{{ 'org.warehouses.filters.q' | transloco }}</mat-label>
            <mat-icon matPrefix aria-hidden="true">search</mat-icon>
            <input
              matInput
              type="search"
              autocomplete="off"
              [formControl]="q"
              data-testid="warehouse-filter-q"
            />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.warehouses.filters.legalEntity' | transloco }}</mat-label>
            <mat-select
              [value]="legalEntityId()"
              (selectionChange)="onLegalEntityFilter($event.value)"
              data-testid="warehouse-filter-legal-entity"
              [panelWidth]="null"
            >
              <mat-select-trigger>{{
                legalEntityFilterLabel() || ('org.warehouses.filters.all' | transloco)
              }}</mat-select-trigger>
              <mat-option [value]="all">{{ 'org.warehouses.filters.all' | transloco }}</mat-option>
              @for (legalEntity of legalEntities(); track legalEntity.id) {
                <mat-option [value]="legalEntity.id">
                  <span class="nx-mono nx-wh-list__code">{{ legalEntity.code }}</span>
                  {{ legalEntity.legalName }}
                </mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.warehouses.filters.businessUnit' | transloco }}</mat-label>
            <mat-select
              [value]="businessUnitId()"
              (selectionChange)="businessUnitId.set($event.value)"
              data-testid="warehouse-filter-business-unit"
              [panelWidth]="null"
            >
              <mat-select-trigger>{{
                businessUnitFilterLabel() || ('org.warehouses.filters.all' | transloco)
              }}</mat-select-trigger>
              <mat-option [value]="all">{{ 'org.warehouses.filters.all' | transloco }}</mat-option>
              @for (unit of businessUnitChoices(); track unit.id) {
                <mat-option [value]="unit.id">
                  <span class="nx-mono nx-wh-list__code">{{ unit.code }}</span> {{ caption(unit) }}
                </mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.warehouses.filters.status' | transloco }}</mat-label>
            <mat-select
              [value]="status()"
              (selectionChange)="status.set($event.value)"
              data-testid="warehouse-filter-status"
            >
              <!-- Explicit trigger: the selected text follows the language even if it loads after the select. -->
              <mat-select-trigger>{{ statusLabelKeys[status()] | transloco }}</mat-select-trigger>
              @for (option of statusOptions; track option) {
                <mat-option [value]="option">{{ statusLabelKeys[option] | transloco }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </nx-filter-bar>

        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [filters]="filters()"
          [cardFields]="cardFields"
          emptyIcon="warehouse"
          [emptyMessageKey]="'org.warehouses.empty'"
          [emptyHintKey]="'org.warehouses.emptyHint'"
          [noResultsMessageKey]="'org.warehouses.noResults'"
          (rowOpen)="open($event)"
          (clearFilters)="clearFilters()"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.warehouses.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  styles: `
    .nx-wh-list__code {
      margin-right: var(--nx-space-1);
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WarehouseListPage {
  private readonly facade = inject(WarehouseFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly i18n = inject(NxI18nService);

  protected readonly all = ALL;

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  protected readonly q = new FormControl('', { nonNullable: true });
  private readonly qValue = toSignal(
    this.q.valueChanges.pipe(
      debounceTime(WAREHOUSE_SEARCH_DEBOUNCE_MS),
      map((value) => value.trim()),
    ),
    { initialValue: '' },
  );

  protected readonly legalEntityId = signal<string>(ALL);
  protected readonly businessUnitId = signal<string>(ALL);
  protected readonly status = signal<WarehouseStatusFilter>('all');
  protected readonly statusOptions: readonly WarehouseStatusFilter[] = [
    'all',
    'active',
    'inactive',
  ];
  protected readonly statusLabelKeys: Readonly<Record<WarehouseStatusFilter, string>> = {
    all: 'org.warehouses.filters.statusAll',
    active: 'org.warehouses.filters.statusActive',
    inactive: 'org.warehouses.filters.statusInactive',
  };

  /** Reference lists for the filters and the name columns; empty if they cannot be read. */
  protected readonly legalEntities = toSignal(
    this.facade.searchLegalEntities('').pipe(catchError(() => of<LegalEntityListItem[]>([]))),
    { initialValue: [] as LegalEntityListItem[] },
  );
  private readonly businessUnits = toSignal(
    this.facade
      .searchBusinessUnits(null)('')
      .pipe(catchError(() => of<BusinessUnitListItem[]>([]))),
    { initialValue: [] as BusinessUnitListItem[] },
  );

  private readonly legalEntityById = computed(
    () => new Map(this.legalEntities().map((legalEntity) => [legalEntity.id, legalEntity])),
  );
  private readonly businessUnitById = computed(
    () => new Map(this.businessUnits().map((unit) => [unit.id, unit])),
  );

  /** Business units of the legal entity chosen in the filter (all of them without one). */
  protected readonly businessUnitChoices = computed(() => {
    const legalEntityId = this.legalEntityId();
    return this.businessUnits().filter(
      (unit) => !legalEntityId || unit.legalEntityId === legalEntityId,
    );
  });

  protected readonly legalEntityFilterLabel = computed(() => {
    const legalEntity = this.legalEntityById().get(this.legalEntityId());
    return legalEntity ? `${legalEntity.code} ${legalEntity.legalName}` : '';
  });

  protected readonly businessUnitFilterLabel = computed(() => {
    const unit = this.businessUnitById().get(this.businessUnitId());
    return unit ? `${unit.code} ${this.caption(unit)}` : '';
  });

  protected readonly filters = computed<Record<string, unknown>>(() => {
    const filters: WarehouseListFilters = {
      q: this.qValue() || undefined,
      legalEntityId: this.legalEntityId() || undefined,
      businessUnitId: this.businessUnitId() || undefined,
      isActive: this.status() === 'all' ? undefined : this.status() === 'active',
    };
    return { ...filters };
  });

  protected readonly hasFilters = computed(
    () =>
      this.qValue() !== '' ||
      this.legalEntityId() !== ALL ||
      this.businessUnitId() !== ALL ||
      this.status() !== 'all',
  );

  /**
   * Re-built when the language or the reference lists change: the name and business-unit
   * captions follow the active language, and the legal-entity/business-unit names appear as
   * soon as their lists are read (new renderer params make AG Grid redraw the cells).
   */
  protected readonly columns = computed<NxColDef<WarehouseListItem>[]>(() => {
    const language = this.i18n.activeLang();
    const legalEntities = this.legalEntityById();
    const businessUnits = this.businessUnitById();
    return [
      {
        field: 'code',
        headerKey: 'org.warehouses.fields.code',
        cellClass: 'nx-mono',
        width: 180,
        sortable: true,
      },
      {
        colId: 'caption',
        headerKey: 'org.warehouses.fields.caption',
        valueGetter: (params) =>
          resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
        flex: 2,
        sortable: false,
      },
      {
        colId: 'legalEntity',
        headerKey: 'org.warehouses.fields.legalEntityId',
        cellRenderer: OrgNameCellComponent,
        cellRendererParams: {
          primary: (row: WarehouseListItem | undefined) =>
            (row && legalEntities.get(row.legalEntityId)?.legalName) || '—',
          secondary: (row: WarehouseListItem | undefined) =>
            (row && legalEntities.get(row.legalEntityId)?.code) || null,
        },
        flex: 2,
        sortable: false,
      },
      {
        colId: 'businessUnit',
        headerKey: 'org.warehouses.fields.businessUnitId',
        cellRenderer: OrgNameCellComponent,
        cellRendererParams: {
          primary: (row: WarehouseListItem | undefined) => {
            const unit = row?.businessUnitId ? businessUnits.get(row.businessUnitId) : undefined;
            return unit ? resolveCaption(unit.captionI18n, language, unit.code) : '—';
          },
          secondary: (row: WarehouseListItem | undefined) =>
            (row?.businessUnitId && businessUnits.get(row.businessUnitId)?.code) || null,
        },
        flex: 2,
        sortable: false,
      },
      {
        field: 'isActive',
        headerKey: 'org.warehouses.fields.isActive',
        cellRenderer: OrgActiveStatusCellComponent,
        width: 140,
        sortable: true,
      },
    ];
  });

  /** Cards below 600 px: name, code underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'caption',
    subtitle: 'code',
    status: 'isActive',
  };

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: Record<string, unknown>,
  ): Observable<Page<WarehouseListItem>> =>
    this.facade
      .list(page, pageSize, sort, filters as WarehouseListFilters)
      .pipe(tap((result) => this.total.set(result.total)));

  protected caption(unit: Pick<BusinessUnitListItem, 'captionI18n' | 'code'>): string {
    return resolveCaption(unit.captionI18n, this.i18n.activeLang(), unit.code);
  }

  /** A business unit of another legal entity would give no results: the unit filter is reset. */
  protected onLegalEntityFilter(id: string): void {
    this.legalEntityId.set(id);
    const unit = this.businessUnitById().get(this.businessUnitId());
    if (unit && id && unit.legalEntityId !== id) {
      this.businessUnitId.set(ALL);
    }
  }

  protected clearFilters(): void {
    this.q.setValue('');
    this.legalEntityId.set(ALL);
    this.businessUnitId.set(ALL);
    this.status.set('all');
  }

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<WarehouseListItem, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
