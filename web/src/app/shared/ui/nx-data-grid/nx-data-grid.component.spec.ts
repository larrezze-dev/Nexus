import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { ColDef, SelectionChangedEvent, SortChangedEvent } from 'ag-grid-community';
import { Subject, of } from 'rxjs';
import { NxDataGridComponent } from './nx-data-grid.component';
import { Page } from '../page.model';

interface Row {
  readonly id: string;
  readonly name: string;
}

const columns: ColDef<Row>[] = [{ field: 'name' }];

function createFixture(): ComponentFixture<NxDataGridComponent<Row>> {
  return TestBed.createComponent(NxDataGridComponent<Row>);
}

describe('NxDataGridComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        NxDataGridComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { shared: { dataGrid: { empty: 'No rows' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }).compileComponents();
  });

  it('fetches page 0 on init with the default page size, no sort and the given filters', async () => {
    const fetchSpy = vi.fn((page: number, pageSize: number, sort, filters) =>
      of<Page<Row>>({ items: [{ id: '1', name: 'A' }], page, pageSize, total: 1 }),
    );
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetchSpy);
    fixture.componentRef.setInput('filters', { q: 'abc' });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(0, 25, null, { q: 'abc' });
  });

  it('fetches with the caller-provided pageSize, not the 25 default', async () => {
    const fetchSpy = vi.fn((page: number, pageSize: number) =>
      of<Page<Row>>({ items: [], page, pageSize, total: 0 }),
    );
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetchSpy);
    fixture.componentRef.setInput('pageSize', 50);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(0, 50, null, {});
  });

  it('refetches at the new page/pageSize when the paginator changes', async () => {
    const fetchSpy = vi.fn((page: number, pageSize: number) =>
      of<Page<Row>>({ items: [], page, pageSize, total: 0 }),
    );
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetchSpy);
    fixture.detectChanges();
    await fixture.whenStable();
    fetchSpy.mockClear();

    fixture.componentInstance.onPage({ pageIndex: 2, pageSize: 50, length: 0, previousPageIndex: 0 });
    await fixture.whenStable();

    expect(fetchSpy).toHaveBeenCalledWith(2, 50, null, {});
  });

  it('refetches from page 0 with the new sort model when the grid sort changes', async () => {
    const fetchSpy = vi.fn((page: number, pageSize: number) =>
      of<Page<Row>>({ items: [], page, pageSize, total: 0 }),
    );
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', fetchSpy);
    fixture.detectChanges();
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

  it('emits the selected rows on selectionChanged', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', () => of<Page<Row>>({ items: [], page: 0, pageSize: 25, total: 0 }));
    fixture.detectChanges();

    const emitted: Row[][] = [];
    fixture.componentInstance.selectionChanged.subscribe((rows) => emitted.push(rows));

    const selected: Row[] = [{ id: '1', name: 'A' }];
    const fakeSelectionEvent = {
      api: { getSelectedRows: () => selected },
    } as unknown as SelectionChangedEvent<Row>;
    fixture.componentInstance.onSelectionChanged(fakeSelectionEvent);

    expect(emitted).toEqual([selected]);
  });

  it('shows the empty state when the fetched page has no rows', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput(
      'fetch',
      () => of<Page<Row>>({ items: [], page: 0, pageSize: 25, total: 0 }),
    );
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('[data-testid="nx-data-grid-empty"]');
    expect(empty).toBeTruthy();
    expect(empty?.textContent).toContain('No rows');
  });

  it('shows the loading state while the fetch is pending and hides it once it resolves', async () => {
    const subject = new Subject<Page<Row>>();
    const fixture = createFixture();
    fixture.componentRef.setInput('columns', columns);
    fixture.componentRef.setInput('fetch', () => subject.asObservable());
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-data-grid-loading"]')).toBeTruthy();

    subject.next({ items: [{ id: '1', name: 'A' }], page: 0, pageSize: 25, total: 1 });
    subject.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-data-grid-loading"]')).toBeFalsy();
  });
});
