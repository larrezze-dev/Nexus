import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable, debounceTime, map, tap } from 'rxjs';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { countryName } from '../../../shared/ui/country/country-names';
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
import { LegalEntitiesFacade, LegalEntityListFilters } from './legal-entities.facade';
import { OrgNameCellComponent, secondaryName } from '../shared/org-name-cell.component';

/** Status filter: all, only active, only inactive (`isActive` query parameter). */
export type LegalEntityStatusFilter = 'all' | 'active' | 'inactive';

/** Debounce of the search box before the list reloads. */
export const LEGAL_ENTITY_SEARCH_DEBOUNCE_MS = 300;

/**
 * Legal-entity list (`conventions-frontend.md §4.4`, list template): header with the record
 * count and «New legal entity», a filter bar (search by code or legal name, status) and the
 * server-paginated `nx-data-grid`. The API sorts by `code` and `legalName` only, so the other
 * columns are not sortable. A row (click, Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-legal-entity-list-page',
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
      <nx-page-header [breadcrumb]="true" [title]="'org.legalEntities.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="le-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.legalEntities.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.legalEntities.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-filter-bar [showClear]="hasFilters()" (clear)="clearFilters()">
          <mat-form-field class="nx-filter--search">
            <mat-label>{{ 'org.legalEntities.filters.q' | transloco }}</mat-label>
            <mat-icon matPrefix aria-hidden="true">search</mat-icon>
            <input matInput type="search" autocomplete="off" [formControl]="q" data-testid="le-filter-q" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.legalEntities.filters.status' | transloco }}</mat-label>
            <mat-select [value]="status()" (selectionChange)="status.set($event.value)" data-testid="le-filter-status">
              <!-- Explicit trigger: the selected text follows the language even if it loads after the select. -->
              <mat-select-trigger>{{ statusLabelKeys[status()] | transloco }}</mat-select-trigger>
              @for (option of statusOptions; track option) {
                <mat-option [value]="option">{{ statusLabelKeys[option] | transloco }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </nx-filter-bar>

        <nx-data-grid
          [columns]="columns"
          [fetch]="fetch"
          [filters]="filters()"
          [cardFields]="cardFields"
          emptyIcon="apartment"
          [emptyMessageKey]="'org.legalEntities.empty'"
          [emptyHintKey]="'org.legalEntities.emptyHint'"
          [noResultsMessageKey]="'org.legalEntities.noResults'"
          (rowOpen)="open($event)"
          (clearFilters)="clearFilters()"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.legalEntities.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityListPage {
  private readonly facade = inject(LegalEntitiesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  protected readonly q = new FormControl('', { nonNullable: true });
  private readonly qValue = toSignal(
    this.q.valueChanges.pipe(
      debounceTime(LEGAL_ENTITY_SEARCH_DEBOUNCE_MS),
      map((value) => value.trim()),
    ),
    { initialValue: '' },
  );
  protected readonly status = signal<LegalEntityStatusFilter>('all');
  protected readonly statusOptions: readonly LegalEntityStatusFilter[] = ['all', 'active', 'inactive'];
  protected readonly statusLabelKeys: Readonly<Record<LegalEntityStatusFilter, string>> = {
    all: 'org.legalEntities.filters.statusAll',
    active: 'org.legalEntities.filters.statusActive',
    inactive: 'org.legalEntities.filters.statusInactive',
  };

  protected readonly filters = computed<Record<string, unknown>>(() => {
    const filters: LegalEntityListFilters = {
      q: this.qValue() || undefined,
      isActive: this.status() === 'all' ? undefined : this.status() === 'active',
    };
    return { ...filters };
  });

  protected readonly hasFilters = computed(() => this.qValue() !== '' || this.status() !== 'all');

  protected readonly columns: NxColDef<LegalEntityListItem>[] = [
    {
      field: 'code',
      headerKey: 'org.legalEntities.fields.code',
      cellClass: 'nx-mono',
      width: 180,
      sortable: true,
    },
    {
      field: 'legalName',
      headerKey: 'org.legalEntities.fields.legalName',
      cellRenderer: OrgNameCellComponent,
      cellRendererParams: {
        primary: (row: LegalEntityListItem | undefined) => row?.legalName ?? '',
        secondary: secondaryName,
      },
      flex: 2,
      sortable: true,
    },
    {
      field: 'currencyCode',
      headerKey: 'org.legalEntities.fields.currencyCode',
      width: 130,
      sortable: false,
    },
    {
      field: 'countryCode',
      headerKey: 'org.legalEntities.fields.countryCode',
      flex: 1,
      sortable: false,
      valueFormatter: (params) => countryName(params.value as string, this.transloco.getActiveLang()),
    },
    {
      field: 'isActive',
      headerKey: 'org.legalEntities.fields.isActive',
      cellRenderer: OrgActiveStatusCellComponent,
      width: 140,
      sortable: false,
    },
  ];

  /** Cards below 600 px: legal name, code underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'legalName',
    subtitle: 'code',
    status: 'isActive',
  };

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: Record<string, unknown>,
  ): Observable<Page<LegalEntityListItem>> =>
    this.facade
      .list(page, pageSize, sort, filters as LegalEntityListFilters)
      .pipe(tap((result) => this.total.set(result.total)));

  protected clearFilters(): void {
    this.q.setValue('');
    this.status.set('all');
  }

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<LegalEntityListItem, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
