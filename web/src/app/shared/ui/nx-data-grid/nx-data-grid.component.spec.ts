import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  Translation,
  TranslocoLoader,
  TranslocoService,
  TranslocoTestingModule,
  provideTransloco,
} from '@jsverse/transloco';
import { AgGridAngular, type ICellRendererAngularComp } from 'ag-grid-angular';
import type { CellKeyDownEvent, ColDef, ICellRendererParams, RowClickedEvent, SortChangedEvent } from 'ag-grid-community';
import { Observable, Subject, of, throwError } from 'rxjs';
import { NxDataGridComponent } from './nx-data-grid.component';
import { Page } from '../page.model';

interface Row {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly active: boolean;
}

@Component({
  selector: 'nx-test-status-cell',
  standalone: true,
  template: `<span class="test-status">{{ label() }}</span>`,
})
class TestStatusCellComponent implements ICellRendererAngularComp {
  protected readonly label = signal('');
  agInit(params: ICellRendererParams<Row, boolean>): void {
    this.label.set(params.value ? 'ON' : 'OFF');
  }
  refresh(params: ICellRendererParams<Row, boolean>): boolean {
    this.agInit(params);
    return true;
  }
}

const columns: ColDef<Row>[] = [
  { field: 'code', headerName: 'Code', sortable: true },
  { field: 'name', headerName: 'Name', sortable: true },
  { field: 'active', headerName: 'Status', width: 140, cellRenderer: TestStatusCellComponent },
];

const ROW_A: Row = { id: '1', code: 'LE-A', name: 'Alpha AG', active: true };
const ROW_B: Row = { id: '2', code: 'LE-B', name: 'Beta SA', active: false };

const EN = {
  shared: {
    dataGrid: { empty: 'No rows', retry: 'Retry' },
    grid: { loadingOoo: 'Loading…' },
    layout: { filterBar: { clear: 'Clear filters' } },
    emptyState: {
      noResults: { title: 'Nothing matches', message: 'Try other filters.' },
      error: { title: 'Could not load', message: 'Try again.' },
      empty: { title: 'Empty', message: 'Nothing yet.' },
    },
  },
  custom: { empty: 'No warehouses yet', hint: 'Create the first one.' },
};

function page(items: Row[], total = items.length): Observable<Page<Row>> {
  return of({ items, page: 0, pageSize: 25, total });
}

function el(fixture: ComponentFixture<unknown>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

describe('NxDataGridComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        NxDataGridComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: EN },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }).compileComponents();
  });

  function create(fetch: unknown, extra: Record<string, unknown> = {}): ComponentFixture<NxDataGridComponent<Row>> {
    const fixture = TestBed.createComponent(NxDataGridComponent<Row>);
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetch);
    for (const [name, value] of Object.entries(extra)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('fetches page 0 on init with the default page size, no sort and the given filters', async () => {
    const fetchSpy = vi.fn(() => page([ROW_A]));
    const fixture = create(fetchSpy, { filters: { q: 'abc' } });
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(0, 25, null, { q: 'abc' });
  });

  it('fetches with the caller-provided pageSize, not the 25 default', async () => {
    const fetchSpy = vi.fn(() => page([]));
    const fixture = create(fetchSpy, { pageSize: 50 });
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(0, 50, null, {});
  });

  it('refetches at the new page/pageSize when the paginator changes', async () => {
    const fetchSpy = vi.fn(() => page([]));
    const fixture = create(fetchSpy);
    await fixture.whenStable();
    fetchSpy.mockClear();

    fixture.componentInstance.onPage({ pageIndex: 2, pageSize: 50, length: 0, previousPageIndex: 0 });
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(2, 50, null, {});
  });

  it('stays on the page the user picked (no bounce back to page 0)', async () => {
    const fetchSpy = vi.fn(() => page([ROW_A], 100));
    const fixture = create(fetchSpy);
    await settle(fixture);
    fetchSpy.mockClear();

    fixture.componentInstance.onPage({ pageIndex: 2, pageSize: 25, length: 100, previousPageIndex: 0 });
    await settle(fixture);

    expect(fetchSpy.mock.calls).toEqual([[2, 25, null, {}]]);
  });

  it('refetches from page 0 with the new sort model when the grid sort changes', async () => {
    const fetchSpy = vi.fn(() => page([]));
    const fixture = create(fetchSpy);
    await fixture.whenStable();
    fixture.componentInstance.onPage({ pageIndex: 3, pageSize: 25, length: 0, previousPageIndex: 0 });
    await fixture.whenStable();
    fetchSpy.mockClear();

    const fakeSortEvent = {
      api: { getColumnState: () => [{ colId: 'name', sort: 'desc' }] },
    } as unknown as SortChangedEvent<Row>;
    fixture.componentInstance.onSortChanged(fakeSortEvent);
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(0, 25, { colId: 'name', direction: 'desc' }, {});
  });

  it('does not refetch when the grid reports the sort it already has (re-applied after re-creation)', async () => {
    const fetchSpy = vi.fn(() => page([]));
    const fixture = create(fetchSpy);
    await fixture.whenStable();
    const sortEvent = { api: { getColumnState: () => [{ colId: 'name', sort: 'asc' }] } } as unknown as SortChangedEvent<Row>;
    fixture.componentInstance.onSortChanged(sortEvent);
    await fixture.whenStable();
    fetchSpy.mockClear();

    fixture.componentInstance.onSortChanged(sortEvent);
    await fixture.whenStable();

    expect(fetchSpy).not.toHaveBeenCalled();
  });

  describe('opening a row', () => {
    it('emits rowOpen and the legacy selectionChanged([row]) on a row click', () => {
      const fixture = create(() => page([]));
      const opened: Row[] = [];
      const selected: Row[][] = [];
      fixture.componentInstance.rowOpen.subscribe((row) => opened.push(row));
      fixture.componentInstance.selectionChanged.subscribe((rows) => selected.push(rows));

      fixture.componentInstance.onRowClicked({ data: ROW_A } as RowClickedEvent<Row>);

      expect(opened).toEqual([ROW_A]);
      expect(selected).toEqual([[ROW_A]]);
    });

    it('opens the row on Enter in a focused cell, and ignores other keys', () => {
      const fixture = create(() => page([]));
      const opened: Row[] = [];
      fixture.componentInstance.rowOpen.subscribe((row) => opened.push(row));

      const key = (k: string) =>
        ({ data: ROW_B, event: new KeyboardEvent('keydown', { key: k }) }) as unknown as CellKeyDownEvent<Row>;
      fixture.componentInstance.onCellKeyDown(key('ArrowDown'));
      fixture.componentInstance.onCellKeyDown(key('Enter'));

      expect(opened).toEqual([ROW_B]);
    });
  });

  describe('states', () => {
    it('shows skeleton rows (with the column headers, no visible «Loading» text) while fetching', async () => {
      const subject = new Subject<Page<Row>>();
      const fixture = create(() => subject.asObservable());
      await settle(fixture);

      const skeleton = el(fixture).querySelector('[data-testid="nx-data-grid-loading"]') as HTMLElement;
      expect(skeleton).toBeTruthy();
      expect(skeleton.getAttribute('role')).toBe('status');
      expect(skeleton.querySelectorAll('.nx-data-grid__skeleton-row').length).toBe(5);
      expect(skeleton.querySelector('.nx-data-grid__skeleton-head')?.textContent).toContain('Code');
      // The only «Loading…» text is the screen-reader one.
      expect(skeleton.querySelector('.nx-data-grid__sr-only')?.textContent).toContain('Loading…');
      expect(el(fixture).querySelector('.ag-overlay-loading-center')).toBeNull();

      subject.next({ items: [ROW_A], page: 0, pageSize: 25, total: 1 });
      subject.complete();
      fixture.detectChanges();

      expect(el(fixture).querySelector('[data-testid="nx-data-grid-loading"]')).toBeFalsy();
    });

    it('shows the empty state with the caller texts when there are no records and no filters', async () => {
      const fixture = create(() => page([]), { emptyMessageKey: 'custom.empty', emptyHintKey: 'custom.hint' });
      await settle(fixture);

      const empty = el(fixture).querySelector('[data-testid="nx-data-grid-empty"]') as HTMLElement;
      expect(empty.getAttribute('data-kind')).toBe('empty');
      expect(empty.textContent).toContain('No warehouses yet');
      expect(empty.textContent).toContain('Create the first one.');
      expect(el(fixture).querySelector('mat-paginator')).toBeNull();
      expect(fixture.componentInstance.state()).toBe('empty');
    });

    it('keeps the default empty title from shared.dataGrid.empty', async () => {
      const fixture = create(() => page([]));
      await settle(fixture);

      expect(el(fixture).querySelector('[data-testid="nx-data-grid-empty"]')?.textContent).toContain('No rows');
    });

    it('shows «no results» instead of «empty» when a filter is set', async () => {
      const fixture = create(() => page([]), { filters: { q: 'basel', status: undefined } });
      await settle(fixture);

      const noResults = el(fixture).querySelector('[data-testid="nx-data-grid-no-results"]') as HTMLElement;
      expect(noResults.textContent).toContain('Nothing matches');
      expect(el(fixture).querySelector('[data-testid="nx-data-grid-empty"]')).toBeNull();
      // Nobody listens to (clearFilters): no button.
      expect(noResults.querySelector('[data-testid="nx-data-grid-clear-filters"]')).toBeNull();
    });

    it('treats only undefined / null / empty values as «no filter», unless hasActiveFilters overrides it', async () => {
      const fixture = create(() => page([]), { filters: { q: '', type: null, tags: [] } });
      await settle(fixture);
      expect(fixture.componentInstance.state()).toBe('empty');

      fixture.componentRef.setInput('filters', { refresh: 1 });
      fixture.componentRef.setInput('hasActiveFilters', false);
      await settle(fixture);
      expect(fixture.componentInstance.state()).toBe('empty');
    });

    it('shows an error state announced as an alert, and «Retry» refetches the same page', async () => {
      let fail = true;
      const fetchSpy = vi.fn(() => (fail ? throwError(() => new Error('boom')) : page([ROW_A])));
      const fixture = create(fetchSpy);
      await settle(fixture);

      const error = el(fixture).querySelector('[data-testid="nx-data-grid-error"]') as HTMLElement;
      expect(error.getAttribute('role')).toBe('alert');
      expect(error.textContent).toContain('Could not load');

      fail = false;
      (error.querySelector('[data-testid="nx-data-grid-retry"]') as HTMLButtonElement).click();
      await settle(fixture);

      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(el(fixture).querySelector('[data-testid="nx-data-grid-error"]')).toBeNull();
      expect(fixture.componentInstance.state()).toBe('rows');
    });

    it('shows the paginator under the rows', async () => {
      const fixture = create(() => page([ROW_A, ROW_B], 30));
      await settle(fixture);

      expect(el(fixture).querySelector('mat-paginator')).toBeTruthy();
    });
  });

  describe('layout', () => {
    it('gives columns without width/flex flex:1 so the table fills the width, and keeps fixed widths', () => {
      const fixture = create(() => page([]));
      const grid = fixture.debugElement.query(By.directive(AgGridAngular)).componentInstance as AgGridAngular<Row>;
      const defs = grid.columnDefs as ColDef<Row>[];

      expect(defs[0].flex).toBe(1);
      expect(defs[1].flex).toBe(1);
      expect(defs[2].flex).toBeUndefined();
      expect(defs[2].width).toBe(140);
    });

    it('renders the table with autoHeight (the page of rows is never cut off)', () => {
      const fixture = create(() => page([]));
      const grid = fixture.debugElement.query(By.directive(AgGridAngular)).componentInstance as AgGridAngular<Row>;

      expect(grid.domLayout).toBe('autoHeight');
    });

    it('renders one card per row (title = 1st column, subtitle = 2nd, status = component column) that opens the row', async () => {
      const fixture = create(() => page([ROW_A, ROW_B]));
      await settle(fixture);
      const opened: Row[] = [];
      fixture.componentInstance.rowOpen.subscribe((row) => opened.push(row));

      const cards = el(fixture).querySelectorAll<HTMLButtonElement>('[data-testid="nx-data-grid-cards"] button');
      expect(cards.length).toBe(2);
      expect(cards[0].querySelector('.nx-data-grid__card-title')?.textContent).toContain('LE-A');
      expect(cards[0].querySelector('.nx-data-grid__card-subtitle')?.textContent).toContain('Alpha AG');
      expect(cards[0].querySelector('.test-status')?.textContent).toBe('ON');
      expect(cards[1].querySelector('.test-status')?.textContent).toBe('OFF');

      cards[1].click();
      expect(opened).toEqual([ROW_B]);
    });

    it('lets the caller pick the card columns', async () => {
      const fixture = create(() => page([ROW_A]), { cardFields: { title: 'name', subtitle: 'code', status: null } });
      await settle(fixture);

      const card = el(fixture).querySelector('[data-testid="nx-data-grid-cards"] button') as HTMLElement;
      expect(card.querySelector('.nx-data-grid__card-title')?.textContent).toContain('Alpha AG');
      expect(card.querySelector('.nx-data-grid__card-subtitle')?.textContent).toContain('LE-A');
      expect(card.querySelector('.test-status')).toBeNull();
    });
  });
});

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxDataGridComponent],
  template: `
    <nx-data-grid [columns]="columns" [fetch]="fetch" [filters]="{ q: 'x' }" (clearFilters)="cleared = cleared + 1">
      <button nxEmptyAction type="button">New</button>
    </nx-data-grid>
  `,
})
class ClearFiltersHostComponent {
  readonly columns = columns;
  readonly fetch = () => page([]);
  cleared = 0;
}

describe('NxDataGridComponent «Clear filters»', () => {
  it('offers «Clear filters» in the no-results state when the page listens to (clearFilters)', async () => {
    await TestBed.configureTestingModule({
      imports: [
        ClearFiltersHostComponent,
        TranslocoTestingModule.forRoot({ langs: { en: EN }, translocoConfig: { availableLangs: ['en'], defaultLang: 'en' } }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ClearFiltersHostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = el(fixture).querySelector('[data-testid="nx-data-grid-clear-filters"]') as HTMLButtonElement;
    expect(button.textContent).toContain('Clear filters');
    button.click();
    expect(fixture.componentInstance.cleared).toBe(1);
  });
});

describe('NxDataGridComponent i18n', () => {
  function gridOf(fixture: ComponentFixture<unknown>): AgGridAngular<Row> {
    return fixture.debugElement.query(By.directive(AgGridAngular)).componentInstance as AgGridAngular<Row>;
  }

  it('re-creates the grid on language change with the new localeText, keeping sort and page without refetching', async () => {
    await TestBed.configureTestingModule({
      imports: [
        NxDataGridComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            de: { shared: { dataGrid: { empty: 'Leer' }, grid: { loadingOoo: 'Wird geladen …' } } },
            es: { shared: { dataGrid: { empty: 'Vacío' }, grid: { loadingOoo: 'Cargando…' } } },
          },
          translocoConfig: { availableLangs: ['de', 'es'], defaultLang: 'de' },
        }),
      ],
    }).compileComponents();

    const fetchSpy = vi.fn((pageIndex: number) => of<Page<Row>>({ items: [ROW_A, ROW_B], page: pageIndex, pageSize: 25, total: 60 }));
    const fixture = TestBed.createComponent(NxDataGridComponent<Row>);
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetchSpy);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(gridOf(fixture).localeText?.['loadingOoo']).toBe('Wird geladen …');
    await vi.waitFor(() => expect(fixture.componentInstance.api).toBeTruthy());

    // The user sorts by name (desc) and goes to page 2.
    const firstApi = fixture.componentInstance.api!;
    firstApi.applyColumnState({ state: [{ colId: 'name', sort: 'desc' }], defaultState: { sort: null } });
    await vi.waitFor(() => expect(fetchSpy).toHaveBeenLastCalledWith(0, 25, { colId: 'name', direction: 'desc' }, {}));
    fixture.componentInstance.onPage({ pageIndex: 1, pageSize: 25, length: 60, previousPageIndex: 0 });
    await fixture.whenStable();
    fetchSpy.mockClear();

    const firstGrid = el(fixture).querySelector('ag-grid-angular');
    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el(fixture).querySelector('ag-grid-angular')).not.toBe(firstGrid);
    expect(gridOf(fixture).localeText?.['loadingOoo']).toBe('Cargando…');
    await vi.waitFor(() => expect(fixture.componentInstance.api).not.toBe(firstApi));
    const sorted = fixture.componentInstance.api!.getColumnState().find((state) => state.sort);
    expect(sorted).toMatchObject({ colId: 'name', sort: 'desc' });
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(fixture.debugElement.query(By.css('mat-paginator')).componentInstance.pageIndex).toBe(1);
  });
});

class DelayedLoader implements TranslocoLoader {
  static readonly release = new Subject<void>();
  getTranslation(): Observable<Translation> {
    return new Observable<Translation>((subscriber) => {
      const sub = DelayedLoader.release.subscribe(() => {
        subscriber.next({ shared: { grid: { loadingOoo: 'Wird geladen …' } } });
        subscriber.complete();
      });
      return () => sub.unsubscribe();
    });
  }
}

describe('NxDataGridComponent first load', () => {
  it('gives AG Grid the translated localeText once the translation file arrives (no raw keys)', async () => {
    await TestBed.configureTestingModule({
      imports: [NxDataGridComponent],
      providers: [
        provideTransloco({
          config: { availableLangs: ['de'], defaultLang: 'de', missingHandler: { logMissingKey: false } },
          loader: DelayedLoader,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NxDataGridComponent<Row>);
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', () => new Subject<Page<Row>>());
    fixture.detectChanges();
    const firstGrid = el(fixture).querySelector('ag-grid-angular');

    DelayedLoader.release.next();
    await fixture.whenStable();
    fixture.detectChanges();

    const grid = fixture.debugElement.query(By.directive(AgGridAngular)).componentInstance as AgGridAngular<Row>;
    expect(grid.localeText?.['loadingOoo']).toBe('Wird geladen …');
    expect(el(fixture).querySelector('ag-grid-angular')).not.toBe(firstGrid);
  });
});
