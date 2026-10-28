import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  effect,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule } from '@jsverse/transloco';
import { AgGridAngular } from 'ag-grid-angular';
import {
  AllCommunityModule,
  type ColDef,
  type GridApi,
  type GridReadyEvent,
  ModuleRegistry,
  type SelectionChangedEvent,
  type SortChangedEvent,
  themeMaterial,
} from 'ag-grid-community';
import { Subject, catchError, of, switchMap } from 'rxjs';
import { NxDataGridFetchFn, NxSortModel, Page } from '../page.model';

// AG Grid Community requires modules to be registered explicitly (tree-shaking).
// `AllCommunityModule` only bundles Community (MIT) features — no Enterprise
// module is registered here or anywhere else in this component (ADR-0010).
ModuleRegistry.registerModules([AllCommunityModule]);

/**
 * Generic server-paginated data grid wrapping AG Grid Community.
 *
 * Pagination note (ADR-0010): AG Grid Community does not ship the
 * Server-Side Row Model (that one is Enterprise-only). Instead this
 * component uses the plain Client-Side Row Model with exactly one page of
 * rows loaded at a time (`rowData` is reset to the current page's `items`
 * on every fetch) and drives paging with Angular Material's own
 * `mat-paginator`, calling `fetch(page, pageSize, sort, filters)` again on
 * every page/pageSize/sort/filters change. AG Grid's own pagination UI is
 * never enabled, so there is no double pagination.
 */
@Component({
  selector: 'nx-data-grid',
  standalone: true,
  imports: [AgGridAngular, MatPaginatorModule, MatProgressSpinnerModule, TranslocoModule],
  templateUrl: './nx-data-grid.component.html',
  styleUrl: './nx-data-grid.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxDataGridComponent<T> {
  /** Column definitions, passed straight through to AG Grid's `ColDef<T>`. */
  readonly columns = input.required<ColDef<T>[]>();

  /** Called on init and on every page/sort/filters change to load one page of rows. */
  readonly fetch = input.required<NxDataGridFetchFn<T>>();

  /** Caller-defined filters (search `q`, status, …). Changing this resets to page 0. */
  readonly filters = input<Record<string, unknown>>({});

  /** Initial page size; the user may change it afterwards via the paginator. */
  readonly pageSize = input<number>(25);

  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);

  /** i18n key shown when the current page has no rows. */
  readonly emptyMessageKey = input<string>('shared.dataGrid.empty');

  @Output() readonly selectionChanged = new EventEmitter<T[]>();

  protected readonly theme = themeMaterial;

  protected readonly loading = signal(false);
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

  constructor() {
    // Reruns whenever `filters()` changes (including once, on creation), and
    // always restarts from page 0 so stale rows from a different filter are
    // never shown.
    effect(() => {
      this.filters();
      this.pageIndex.set(0);
      this.reload$.next();
    });

    this.reload$
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          return this.fetch()(this.pageIndex(), this.pageSizeState(), this.sort(), this.filters()).pipe(
            catchError(() =>
              of<Page<T>>({
                items: [],
                page: this.pageIndex(),
                pageSize: this.pageSizeState(),
                total: 0,
              }),
            ),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.rowData.set([...result.items]);
        this.total.set(result.total);
        this.loading.set(false);
      });
  }

  onGridReady(event: GridReadyEvent<T>): void {
    this.gridApi = event.api;
  }

  onSortChanged(event: SortChangedEvent<T>): void {
    const sorted = event.api.getColumnState().find((state) => state.sort);
    this.sort.set(sorted ? { colId: sorted.colId, direction: sorted.sort as 'asc' | 'desc' } : null);
    this.pageIndex.set(0);
    this.reload$.next();
  }

  onSelectionChanged(event: SelectionChangedEvent<T>): void {
    this.selectionChanged.emit(event.api.getSelectedRows());
  }

  onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSizeState.set(event.pageSize);
    this.reload$.next();
  }

  private gridApi: GridApi<T> | null = null;
}
