import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { Page } from '../../../shared/ui/page.model';
import { LegalEntitiesFacade } from './legal-entities.facade';
import { LEGAL_ENTITY_SEARCH_DEBOUNCE_MS, LegalEntityListPage } from './legal-entity-list.page';
import { secondaryName } from './legal-entity-name-cell.component';

const listItem: LegalEntityListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  legalName: 'SAPE smart bud AG',
  displayName: 'SSB Schweiz',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

const en = {
  org: {
    legalEntities: {
      title: 'Legal entities',
      new: 'New legal entity',
      fields: { code: 'Code', legalName: 'Legal name', currencyCode: 'Currency', countryCode: 'Country', isActive: 'Status' },
      filters: { q: 'Search by code or legal name', status: 'Status', statusAll: 'All', statusActive: 'Active', statusInactive: 'Inactive' },
      empty: 'No legal entities yet',
      emptyHint: 'Create the first one.',
      noResults: 'No legal entity matches the filters',
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' }, filterBar: { clear: 'Clear filters', ariaLabel: 'Filters' } } },
};

describe('LegalEntityListPage', () => {
  let fixture: ComponentFixture<LegalEntityListPage>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn(() => of<Page<LegalEntityListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }));

    await TestBed.configureTestingModule({
      imports: [
        LegalEntityListPage,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: LegalEntitiesFacade, useValue: { list } }],
    }).compileComponents();

    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:CREATE', scopes: [] } as never]);
    fixture = TestBed.createComponent(LegalEntityListPage);
  });

  afterEach(() => vi.useRealTimers());

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the list template: one h1, the record count, «New legal entity» and the filter bar', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Legal entities');
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe('1 record');
    expect(root().querySelector('[data-testid="le-list-new"]')?.textContent).toContain('New legal entity');
    expect(root().querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    expect(root().querySelector('nx-filter-bar [data-testid="le-filter-q"]')).toBeTruthy();
    expect(root().querySelector('nx-filter-bar [data-testid="le-filter-status"]')).toBeTruthy();
  });

  it('sends the search (debounced, trimmed) and the status filter to the API', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    list.mockClear();

    const page = fixture.componentInstance as unknown as {
      q: { setValue(value: string): void };
      status: { set(value: string): void };
    };
    page.q.setValue('  sape ');
    vi.advanceTimersByTime(LEGAL_ENTITY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'sape', isActive: undefined });

    page.status.set('inactive');
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'sape', isActive: false });

    page.status.set('active');
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: 'sape', isActive: true });
  });

  it('«Clear filters» resets the search and the status', () => {
    vi.useFakeTimers();
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      q: { value: string; setValue(value: string): void };
      status: { (): string; set(value: string): void };
      clearFilters(): void;
    };
    page.q.setValue('x');
    page.status.set('inactive');
    vi.advanceTimersByTime(LEGAL_ENTITY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(root().querySelector('[data-testid="nx-filter-bar-clear"]')).toBeTruthy();

    page.clearFilters();
    vi.advanceTimersByTime(LEGAL_ENTITY_SEARCH_DEBOUNCE_MS);
    fixture.detectChanges();
    expect(page.q.value).toBe('');
    expect(page.status()).toBe('all');
    expect(list).toHaveBeenLastCalledWith(0, 25, null, { q: undefined, isActive: undefined });
    expect(root().querySelector('[data-testid="nx-filter-bar-clear"]')).toBeNull();
  });

  it('only lets the user sort by the columns the API sorts on (code, legal name)', () => {
    const columns = (fixture.componentInstance as unknown as { columns: { field: string; sortable: boolean }[] }).columns;
    expect(columns.filter((column) => column.sortable).map((column) => column.field)).toEqual(['code', 'legalName']);
  });

  it('opens a row at its detail page', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { open(row: LegalEntityListItem): void }).open(listItem);

    expect(navigate).toHaveBeenCalledWith([listItem.id], { relativeTo: TestBed.inject(ActivatedRoute) });
  });
});

describe('secondaryName', () => {
  it('shows the display name only when it is set and differs from the legal name', () => {
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: 'SSB Schweiz' })).toBe('SSB Schweiz');
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: 'SAPE smart bud AG' })).toBeNull();
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: null })).toBeNull();
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: '  ' })).toBeNull();
  });
});
