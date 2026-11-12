import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { ValueFormatterParams, ValueGetterParams } from 'ag-grid-community';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import type { NxColDef } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import type { Page } from '../../../shared/ui/page.model';
import { PartiesFacade } from './parties.facade';
import { PARTY_SEARCH_DEBOUNCE_MS, PartyListPage } from './party-list.page';

const listItem: PartyListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c20',
  partyType: 'ORGANIZATION',
  legalName: 'Acme Events SA',
  displayName: 'Acme Events',
  isInternal: false,
  isActive: true,
};

const en = {
  org: {
    parties: {
      title: 'Parties',
      new: 'New party',
      empty: 'No parties yet',
      emptyHint: 'Add the first one.',
      noResults: 'No party matches the filters',
      columns: { name: 'Name' },
      fields: { partyType: 'Type', isInternal: 'Scope', isActive: 'Status' },
      type: { ORGANIZATION: 'Organization', PERSON: 'Person' },
      scope: { internal: 'Internal', external: 'External' },
      filters: {
        q: 'Search by name',
        partyType: 'Type',
        partyTypeAll: 'All',
        isInternal: 'Scope',
        isInternalAll: 'All',
        internal: 'Internal',
        external: 'External',
      },
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' }, filterBar: { clear: 'Clear filters', ariaLabel: 'Filters' } } },
};

type PageInternals = {
  q: { value: string; setValue(value: string): void };
  partyType: { (): string; set(value: string): void };
  scope: { (): string; set(value: string): void };
  columns(): NxColDef<PartyListItem>[];
  clearFilters(): void;
  open(row: PartyListItem): void;
};

describe('PartyListPage', () => {
  let fixture: ComponentFixture<PartyListPage>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn(() => of<Page<PartyListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }));

    await TestBed.configureTestingModule({
      imports: [
        PartyListPage,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: PartiesFacade, useValue: { list } }],
    }).compileComponents();

    TestBed.inject(PermissionsStore).setPermissions([{ code: 'PARTY:CREATE', scopes: [] } as never]);
    fixture = TestBed.createComponent(PartyListPage);
  });

  afterEach(() => vi.useRealTimers());

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function page(): PageInternals {
    return fixture.componentInstance as unknown as PageInternals;
  }

  function column(id: string): NxColDef<PartyListItem> {
    const found = page()
      .columns()
      .find((col) => (col.colId ?? col.field) === id);
    if (!found) {
      throw new Error(`no column ${id}`);
    }
    return found;
  }

  it('renders the list template: one h1, the record count, «New party» and the three filters', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Parties');
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe('1 record');
    expect(root().querySelector('[data-testid="party-list-new"]')?.textContent).toContain('New party');
    expect(root().querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    expect(root().querySelector('nx-filter-bar [data-testid="party-filter-q"]')).toBeTruthy();
    expect(root().querySelector('nx-filter-bar [data-testid="party-filter-type"]')).toBeTruthy();
    expect(root().querySelector('nx-filter-bar [data-testid="party-filter-internal"]')).toBeTruthy();
  });

  it('hides «New party» without PARTY:CREATE', () => {
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'PARTY:READ', scopes: [] } as never]);
    fixture.detectChanges();

    expect(root().querySelector('[data-testid="party-list-new"]')).toBeNull();
  });

  it('sends the search (debounced, trimmed), the type and the scope filter to the API', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    list.mockClear();

    page().q.setValue('  acme ');
    vi.advanceTimersByTime(PARTY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'acme', partyType: undefined, isInternal: undefined });

    page().partyType.set('PERSON');
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'acme', partyType: 'PERSON', isInternal: undefined });

    page().scope.set('external');
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'acme', partyType: 'PERSON', isInternal: false });

    page().scope.set('internal');
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'acme', partyType: 'PERSON', isInternal: true });
  });

  it('«Clear filters» resets the search, the type and the scope', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    page().q.setValue('x');
    page().partyType.set('ORGANIZATION');
    page().scope.set('internal');
    vi.advanceTimersByTime(PARTY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(root().querySelector('[data-testid="nx-filter-bar-clear"]')).toBeTruthy();

    page().clearFilters();
    vi.advanceTimersByTime(PARTY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(page().q.value).toBe('');
    expect(page().partyType()).toBe('all');
    expect(page().scope()).toBe('all');
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: undefined, partyType: undefined, isInternal: undefined });
    expect(root().querySelector('[data-testid="nx-filter-bar-clear"]')).toBeNull();
  });

  it('translates the type and the scope instead of showing ORGANIZATION or true/false', () => {
    fixture.detectChanges();
    const format = (id: string, value: unknown) =>
      (column(id).valueFormatter as (params: ValueFormatterParams<PartyListItem>) => string)({
        value,
      } as ValueFormatterParams<PartyListItem>);

    expect(format('partyType', 'ORGANIZATION')).toBe('Organization');
    expect(format('partyType', 'PERSON')).toBe('Person');
    expect(format('isInternal', true)).toBe('Internal');
    expect(format('isInternal', false)).toBe('External');
    expect(column('isInternal').cellDataType).toBe(false);

    const summary = (column('summary').valueGetter as (params: ValueGetterParams<PartyListItem>) => string)({
      data: listItem,
    } as ValueGetterParams<PartyListItem>);
    expect(summary).toBe('Organization · External');
    expect(column('summary').hide).toBe(true);
  });

  it('only lets the user sort by the columns the API sorts on', () => {
    fixture.detectChanges();
    const sortable = page()
      .columns()
      .filter((col) => col.sortable)
      .map((col) => col.field);
    expect(sortable).toEqual(['legalName', 'partyType', 'isInternal']);
  });

  it('opens a row at its detail page', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    page().open(listItem);

    expect(navigate).toHaveBeenCalledWith([listItem.id], { relativeTo: TestBed.inject(ActivatedRoute) });
  });
});
