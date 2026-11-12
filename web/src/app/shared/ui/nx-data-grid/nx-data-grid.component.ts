import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { TranslocoModule } from '@jsverse/transloco';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  type CellKeyDownEvent,
  type FullWidthCellKeyDownEvent,
  type ColDef,
  type GridApi,
  type GridReadyEvent,
  ModuleRegistry,
  type RowClickedEvent,
  type SortChangedEvent,
  themeMaterial,
} from 'ag-grid-community';
import { Subject, catchError, map, of, switchMap } from 'rxjs';
import { NxEmptyStateComponent } from '../layout/nx-empty-state.component';
import { NxDataGridFetchFn, NxSortModel, Page } from '../page.model';
import { NxGridCellDirective, cellText, columnId, hasComponentRenderer, isMonoColumn } from './nx-grid-cell';
import { NxGridLocale } from './nx-grid-locale';

// AG Grid Community requires modules to be registered explicitly (tree-shaking).
// `AllCommunityModule` only bundles Community (MIT) features — no Enterprise
// module is registered here or anywhere else in this component (ADR-0010).
ModuleRegistry.registerModules([AllCommunityModule]);

/** Row height and header height of the table (canvas board 2), in px. */
const ROW_HEIGHT = 52;
const HEADER_HEIGHT = 44;

/**
 * The table grows with the rows of the page up to this many rows, then scrolls inside itself
 * with a fixed header: a page of 25 shows whole, a page of 100 does not make the page 5 000 px long.
 */
export const NX_GRID_MAX_VISIBLE_ROWS = 25;

/** What the grid area shows. */
export type NxDataGridState = 'loading' | 'error' | 'empty' | 'no-results' | 'rows';

/** Which columns the card view (< 600 px) uses; values are column ids (`colId` or `field`). */
export interface NxDataGridCardFields {
  readonly title?: string;
  /** Muted second line; `null` = none. */
  readonly subtitle?: string | null;
  /** Column shown at the right of the card (status badge); `null` = none. */
  readonly status?: string | null;
}

/** AG Grid theme driven by the NEXUS tokens (`--nx-*`), so the grid follows `tokens.css`. */
const NX_GRID_THEME = themeMaterial.withParams({
  fontFamily: 'var(--nx-font-family), sans-serif',
  fontSize: 14,
  backgroundColor: 'var(--nx-color-surface-raised)',
  foregroundColor: 'var(--nx-color-text)',
  accentColor: 'var(--nx-color-primary)',
  borderColor: 'var(--nx-color-border)',
  headerBackgroundColor: 'var(--nx-color-surface-raised)',
  headerTextColor: 'var(--nx-color-text-muted)',
  headerFontSize: 12,
  headerFontWeight: 500,
  headerHeight: HEADER_HEIGHT,
  headerRowBorder: { color: 'var(--nx-color-border)' },
  headerColumnBorder: false,
  rowHeight: ROW_HEIGHT,
  rowBorder: { color: 'var(--nx-color-row-divider)' },
  rowHoverColor: 'var(--nx-color-row-hover)',
  cellHorizontalPadding: 16,
  wrapperBorder: false,
  wrapperBorderRadius: 0,
  autoHeightMinBodyHeight: 0,
  autoHeightMaxBodyHeight: ROW_HEIGHT * NX_GRID_MAX_VISIBLE_ROWS,
});

const SKELETON_BAR_WIDTHS = [72, 54, 86, 62, 78, 48];

/**
 * Generic server-paginated data grid wrapping AG Grid Community (`conventions-frontend.md §4.4`).
 *
 * Pagination note (ADR-0010): AG Grid Community does not ship the Server-Side Row Model (that
 * one is Enterprise-only). Instead this component uses the plain Client-Side Row Model with
 * exactly one page of rows loaded at a time (`rowData` is reset to the current page's `items`
 * on every fetch) and drives paging with Angular Material's own `mat-paginator`, calling
 * `fetch(page, pageSize, sort, filters)` again on every page/pageSize/sort/filters change.
 * AG Grid's own pagination UI is never enabled, so there is no double pagination.
 *
 * Layout: the table grows with the page's rows (up to {@link NX_GRID_MAX_VISIBLE_ROWS}, then it
 * scrolls inside with a fixed header), flexible columns fill the whole width, and the
 * translated paginator sits underneath, right-aligned. Standalone it draws its own card; inside
 * an `.nx-card` (the list template's `nx-card--flush`) it is flush with that card. Below 600 px
 * the rows become cards (title, subtitle, status) — see `cardFields`.
 *
 * States: skeleton rows while loading, `nx-empty-state` for empty / no results (when a filter
 * is set) / error (with «Retry»). Content marked `nxEmptyAction` is shown in the empty state.
 *
 * Opening a row: click, Enter on a focused cell, or tap on a card emits `rowOpen(row)` **and**
 * `selectionChanged([row])` (kept for pages written before `rowOpen`). Listen to one of them.
 */
@Component({
  selector: 'nx-data-grid',
  standalone: true,
  imports: [
    AgGridAngular,
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    TranslocoModule,
    NxEmptyStateComponent,
    NxGridCellDirective,
  ],
  templateUrl: './nx-data-grid.component.html',
  styleUrl: './nx-data-grid.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxDataGridComponent<T> {
  /** Column definitions, passed straight through to AG Grid's `ColDef<T>`. May change (e.g. on language switch). */
  readonly columns = input.required<ColDef<T>[]>();

  /** Called on init and on every page/sort/filters change to load one page of rows. */
  readonly fetch = input.required<NxDataGridFetchFn<T>>();

  /** Caller-defined filters (search `q`, status, …). Changing this resets to page 0. */
  readonly filters = input<Record<string, unknown>>({});

  /** Initial page size; the user may change it afterwards via the paginator. */
  readonly pageSize = input<number>(25);

  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);

  /** i18n key of the title shown when the list has no records at all. */
  readonly emptyMessageKey = input<string>('shared.dataGrid.empty');

  /** i18n key of the sentence under the empty title (e.g. «Create the first one to …»); none by default. */
  readonly emptyHintKey = input<string | null>(null);

  /** Material icon of the empty state (e.g. `warehouse`); defaults to a generic inbox. */
  readonly emptyIcon = input<string | null>(null);

  /** i18n key of the title shown when filters are set and nothing matches. */
  readonly noResultsMessageKey = input<string>('shared.emptyState.noResults.title');

  /**
   * Whether the current `filters` narrow the list (decides «no results» vs «empty»). By default
   * any filter value other than `undefined`, `null`, `''` or `[]` counts as active.
   */
  readonly hasActiveFilters = input<boolean | null>(null);

  /**
   * Columns used by the card view (< 600 px). Default: first column = title, second = subtitle,
   * last column rendered by an Angular component (status badge) = status.
   */
  readonly cardFields = input<NxDataGridCardFields | null>(null);

  /** Emits `[row]` when a row is opened (legacy contract; prefer `rowOpen`). */
  @Output() readonly selectionChanged = new EventEmitter<T[]>();

  /** A row was opened: click, Enter on a focused row, or tap on its card. */
  readonly rowOpen = output<T>();

  /** «Clear filters» in the no-results state. The button only appears when this output is bound. */
  @Output() readonly clearFilters = new EventEmitter<void>();

  protected readonly theme = NX_GRID_THEME;
  protected readonly defaultColDef: ColDef<T> = { minWidth: 120, suppressMovable: true };

  private readonly gridLocale = inject(NxGridLocale);
  protected readonly localeText = this.gridLocale.localeText;
  /**
   * Single-item list keyed by the `localeText` object. AG Grid reads `localeText` at init only,
   * so the grid is re-created when the translations load (no raw keys on first load) and on
   * every language switch. Sort and page live in this component and are re-applied.
   */
  protected readonly gridInstances = computed(() => [this.localeText()]);

  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly rowData = signal<T[]>([]);
  protected readonly total = signal(0);
  protected readonly pageIndex = signal(0);
  // `linkedSignal`, not a plain `signal(this.pageSize())`: a field initializer runs before
  // Angular assigns bound input-signal values, so a plain signal would always capture the
  // input's *default* (25), ignoring whatever `[pageSize]` the caller actually passed.
  // `linkedSignal` recomputes from `pageSize()` whenever it changes, while still letting
  // `onPage` override it locally once the user picks a different page size.
  protected readonly pageSizeState = linkedSignal(() => this.pageSize());

  private readonly sort = signal<NxSortModel | null>(null);
  private readonly reload$ = new Subject<void>();
  private gridApi: GridApi<T> | null = null;

  /** Flexible columns fill the width: a column without its own `width`/`flex` gets `flex: 1`. */
  protected readonly gridColumns = computed<ColDef<T>[]>(() =>
    this.columns().map((column) =>
      column.flex !== undefined || column.width !== undefined || column.initialWidth !== undefined
        ? column
        : { ...column, flex: 1 },
    ),
  );

  protected readonly filtersActive = computed(() => {
    const explicit = this.hasActiveFilters();
    if (explicit !== null) {
      return explicit;
    }
    return Object.values(this.filters()).some(
      (value) => value !== undefined && value !== null && value !== '' && !(Array.isArray(value) && value.length === 0),
    );
  });

  /** What the grid area currently shows. */
  readonly state = computed<NxDataGridState>(() => {
    if (this.loading()) {
      return 'loading';
    }
    if (this.failed()) {
      return 'error';
    }
    if (this.rowData().length === 0) {
      return this.filtersActive() ? 'no-results' : 'empty';
    }
    return 'rows';
  });

  protected readonly showPaginator = computed(
    () => this.state() === 'rows' || (this.state() === 'loading' && this.total() > 0),
  );

  /** Header labels and widths for the skeleton (mirrors the table's columns). */
  protected readonly skeletonColumns = computed(() =>
    this.gridColumns().map((column, index) => ({
      header: column.headerName ?? '',
      flex: column.flex ?? null,
      width: column.flex !== undefined ? null : (column.width ?? column.initialWidth ?? null),
      pill: hasComponentRenderer(column),
      index,
    })),
  );

  /** As many skeleton rows as the previous page had (3–10), 5 on first load. */
  protected readonly skeletonRows = computed(() => {
    const previous = this.rowData().length;
    const count = previous === 0 ? 5 : Math.min(Math.max(previous, 3), 10);
    return Array.from({ length: count }, (_, index) => index);
  });

  /** Columns shown on each card below 600 px. */
  protected readonly cardColumns = computed(() => {
    const columns = this.columns();
    const fields = this.cardFields();
    const byId = (id: string | null | undefined) =>
      id ? columns.find((column) => columnId(column) === id) : undefined;
    const title = byId(fields?.title) ?? columns[0];
    const subtitle = fields?.subtitle !== undefined ? byId(fields.subtitle) : columns[1];
    const status =
      fields?.status !== undefined
        ? byId(fields.status)
        : [...columns]
            .reverse()
            .find((column) => hasComponentRenderer(column) && column !== title && column !== subtitle);
    return { title, subtitle, status };
  });

  protected readonly cellText = cellText;
  protected readonly isMono = isMonoColumn;
  protected readonly hasComponentRenderer = hasComponentRenderer;

  constructor() {
    // Reruns whenever `filters()` changes (including once, on creation), and
    // always restarts from page 0 so stale rows from a different filter are
    // never shown.
    // `untracked`: `reload$.next()` runs the fetch synchronously, and the signals it reads
    // (page, page size, sort) must not become dependencies of this effect — otherwise every
    // page or sort change re-ran it and bounced the grid back to page 0.
    effect(() => {
      this.filters();
      untracked(() => {
        this.pageIndex.set(0);
        this.reload$.next();
      });
    });

    this.reload$
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          return this.fetch()(this.pageIndex(), this.pageSizeState(), this.sort(), this.filters()).pipe(
            map((page) => ({ page, failed: false })),
            catchError(() =>
              of({
                page: { items: [], page: this.pageIndex(), pageSize: this.pageSizeState(), total: 0 } as Page<T>,
                failed: true,
              }),
            ),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe(({ page, failed }) => {
        this.rowData.set([...page.items]);
        this.total.set(page.total);
        this.failed.set(failed);
        this.loading.set(false);
      });
  }

  /** Reloads the current page (the error state's «Retry»). */
  reload(): void {
    this.reload$.next();
  }

  onGridReady(event: GridReadyEvent<T>): void {
    this.gridApi = event.api;
    // A re-created grid (language switch, translations loaded) gets the sort indicator back.
    const sort = this.sort();
    if (sort) {
      event.api.applyColumnState({ state: [{ colId: sort.colId, sort: sort.direction }], defaultState: { sort: null } });
    }
  }

  onSortChanged(event: SortChangedEvent<T>): void {
    const sorted = event.api.getColumnState().find((state) => state.sort);
    const next: NxSortModel | null = sorted ? { colId: sorted.colId, direction: sorted.sort as 'asc' | 'desc' } : null;
    const current = this.sort();
    if (next?.colId === current?.colId && next?.direction === current?.direction) {
      return; // re-applied after a grid re-creation: nothing changed, no refetch
    }
    this.sort.set(next);
    this.pageIndex.set(0);
    this.reload$.next();
  }

  onRowClicked(event: RowClickedEvent<T>): void {
    if (event.data) {
      this.openRow(event.data);
    }
  }

  onCellKeyDown(event: CellKeyDownEvent<T> | FullWidthCellKeyDownEvent<T>): void {
    const keyboard = event.event as KeyboardEvent | null | undefined;
    if (keyboard?.key === 'Enter' && event.data) {
      keyboard.preventDefault();
      this.openRow(event.data);
    }
  }

  /** Opens a row: emits `rowOpen` and, for legacy callers, `selectionChanged([row])`. */
  openRow(row: T): void {
    this.rowOpen.emit(row);
    this.selectionChanged.emit([row]);
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSizeState.set(event.pageSize);
    this.reload$.next();
  }

  protected onClearFilters(): void {
    this.clearFilters.emit();
  }

  /** «Clear filters» is offered only when the page listens to `(clearFilters)`. */
  protected canClearFilters(): boolean {
    return this.clearFilters.observed;
  }

  protected skeletonBarWidth(row: number, column: number): number {
    return SKELETON_BAR_WIDTHS[(row + column * 2) % SKELETON_BAR_WIDTHS.length];
  }

  /** The AG Grid API of the current grid instance (tests, advanced callers). */
  get api(): GridApi<T> | null {
    return this.gridApi;
  }
}
