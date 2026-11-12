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
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import { NxCanDirective } from '../../../shared/auth/can.directive';
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
import { LegalEntityNameCellComponent } from '../legal-entities/legal-entity-name-cell.component';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { PartiesFacade, PartyFilters } from './parties.facade';

/** Party type filter: all, or one `partyType` value. */
export type PartyTypeFilter = 'all' | PartyListItem['partyType'];

/** Scope filter: all, only internal (of the group) or only external parties (`isInternal`). */
export type PartyScopeFilter = 'all' | 'internal' | 'external';

/** Debounce of the search box before the list reloads. */
export const PARTY_SEARCH_DEBOUNCE_MS = 300;

/** i18n key of a party type («Organization», «Person»); never the API literal on screen. */
export function partyTypeKey(type: PartyListItem['partyType'] | null | undefined): string {
  return type === 'PERSON' ? 'org.parties.type.PERSON' : 'org.parties.type.ORGANIZATION';
}

/** i18n key of the scope of a party («Internal» / «External»); never `true`/`false` on screen. */
export function partyScopeKey(isInternal: boolean | null | undefined): string {
  return isInternal ? 'org.parties.scope.internal' : 'org.parties.scope.external';
}

/**
 * Parties directory list (`conventions-frontend.md §4.4`, list template): header with the
 * record count and «New party», a filter bar with the three filters `GET /v1/parties` offers
 * (search on the legal name, type, internal/external) and the server-paginated `nx-data-grid`.
 * The API sorts by legal name, type, scope and status; the status column stays unsortable like
 * the other org lists. Type and scope are translated, never the API literals. A row (click,
 * Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-party-list-page',
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
      <nx-page-header [breadcrumb]="true" [title]="'org.parties.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="party-list-new"
          *nxCan="'PARTY:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.parties.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'PARTY:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.parties.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-filter-bar [showClear]="hasFilters()" (clear)="clearFilters()">
          <mat-form-field class="nx-filter--search">
            <mat-label>{{ 'org.parties.filters.q' | transloco }}</mat-label>
            <mat-icon matPrefix aria-hidden="true">search</mat-icon>
            <input matInput type="search" autocomplete="off" [formControl]="q" data-testid="party-filter-q" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.parties.filters.partyType' | transloco }}</mat-label>
            <mat-select [value]="partyType()" (selectionChange)="partyType.set($event.value)" data-testid="party-filter-type">
              <!-- Explicit trigger: the selected text follows the language even if it loads after the select. -->
              <mat-select-trigger>{{ typeLabelKeys[partyType()] | transloco }}</mat-select-trigger>
              @for (option of typeOptions; track option) {
                <mat-option [value]="option">{{ typeLabelKeys[option] | transloco }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'org.parties.filters.isInternal' | transloco }}</mat-label>
            <mat-select [value]="scope()" (selectionChange)="scope.set($event.value)" data-testid="party-filter-internal">
              <mat-select-trigger>{{ scopeLabelKeys[scope()] | transloco }}</mat-select-trigger>
              @for (option of scopeOptions; track option) {
                <mat-option [value]="option">{{ scopeLabelKeys[option] | transloco }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </nx-filter-bar>

        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [filters]="filters()"
          [cardFields]="cardFields"
          emptyIcon="handshake"
          [emptyMessageKey]="'org.parties.empty'"
          [emptyHintKey]="'org.parties.emptyHint'"
          [noResultsMessageKey]="'org.parties.noResults'"
          (rowOpen)="open($event)"
          (clearFilters)="clearFilters()"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'PARTY:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.parties.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartyListPage {
  private readonly facade = inject(PartiesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  /** Emits once the active language's file is loaded (and on every switch): cell texts follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  protected readonly q = new FormControl('', { nonNullable: true });
  private readonly qValue = toSignal(
    this.q.valueChanges.pipe(
      debounceTime(PARTY_SEARCH_DEBOUNCE_MS),
      map((value) => value.trim()),
    ),
    { initialValue: '' },
  );

  protected readonly partyType = signal<PartyTypeFilter>('all');
  protected readonly typeOptions: readonly PartyTypeFilter[] = ['all', 'ORGANIZATION', 'PERSON'];
  protected readonly typeLabelKeys: Readonly<Record<PartyTypeFilter, string>> = {
    all: 'org.parties.filters.partyTypeAll',
    ORGANIZATION: 'org.parties.type.ORGANIZATION',
    PERSON: 'org.parties.type.PERSON',
  };

  protected readonly scope = signal<PartyScopeFilter>('all');
  protected readonly scopeOptions: readonly PartyScopeFilter[] = ['all', 'internal', 'external'];
  protected readonly scopeLabelKeys: Readonly<Record<PartyScopeFilter, string>> = {
    all: 'org.parties.filters.isInternalAll',
    internal: 'org.parties.filters.internal',
    external: 'org.parties.filters.external',
  };

  protected readonly filters = computed<Record<string, unknown>>(() => {
    const type = this.partyType();
    const scope = this.scope();
    const filters: PartyFilters = {
      q: this.qValue() || undefined,
      partyType: type === 'all' ? undefined : type,
      isInternal: scope === 'all' ? undefined : scope === 'internal',
    };
    return { ...filters };
  });

  protected readonly hasFilters = computed(
    () => this.qValue() !== '' || this.partyType() !== 'all' || this.scope() !== 'all',
  );

  /**
   * Re-built when the language (or its file) changes, so the translated cell texts (type, scope,
   * card line) follow it: new formatter functions make AG Grid redraw the cells.
   */
  protected readonly columns = computed<NxColDef<PartyListItem>[]>(() => {
    this.translation();
    const t = (key: string) => this.transloco.translate(key);
    return [
      {
        field: 'legalName',
        headerKey: 'org.parties.columns.name',
        cellRenderer: LegalEntityNameCellComponent,
        flex: 2,
        sortable: true,
      },
      {
        field: 'partyType',
        headerKey: 'org.parties.fields.partyType',
        flex: 1,
        sortable: true,
        valueFormatter: (params) => t(partyTypeKey(params.value as PartyListItem['partyType'])),
      },
      {
        field: 'isInternal',
        headerKey: 'org.parties.fields.isInternal',
        flex: 1,
        sortable: true,
        // A plain text column (AG Grid would draw a boolean as a checkbox).
        cellDataType: false,
        valueFormatter: (params) => t(partyScopeKey(params.value as boolean)),
      },
      {
        field: 'isActive',
        headerKey: 'org.parties.fields.isActive',
        cellRenderer: OrgActiveStatusCellComponent,
        width: 140,
        sortable: false,
      },
      {
        // Card view only (< 600 px): «Organization · External» under the name.
        colId: 'summary',
        hide: true,
        valueGetter: (params) =>
          params.data
            ? `${t(partyTypeKey(params.data.partyType))} · ${t(partyScopeKey(params.data.isInternal))}`
            : '',
      },
    ];
  });

  /** Cards below 600 px: legal name, «type · scope» underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'legalName',
    subtitle: 'summary',
    status: 'isActive',
  };

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: Record<string, unknown>,
  ): Observable<Page<PartyListItem>> =>
    this.facade.list(page, pageSize, sort, filters).pipe(tap((result) => this.total.set(result.total)));

  protected clearFilters(): void {
    this.q.setValue('');
    this.partyType.set('all');
    this.scope.set('all');
  }

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<PartyListItem, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
