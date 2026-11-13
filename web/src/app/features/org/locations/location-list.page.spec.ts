import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxColDef } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { Page } from '../../../shared/ui/page.model';
import { LocationListPage } from './location-list.page';
import { LocationFacade } from './location.facade';

const row: LocationListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  code: 'LOC-CH-01',
  name: 'Lager Zug',
  city: 'Zug',
  countryCode: 'CH',
  isActive: true,
};

const en = {
  org: {
    locations: {
      title: 'Locations',
      new: 'New location',
      fields: { code: 'Code', name: 'Name', city: 'City', countryCode: 'Country', isActive: 'Status' },
      empty: 'No locations yet',
      emptyHint: 'Create the first one.',
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' } } },
};

describe('LocationListPage', () => {
  let fixture: ComponentFixture<LocationListPage>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn(() => of<Page<LocationListItem>>({ items: [row], page: 0, pageSize: 25, total: 1 }));

    await TestBed.configureTestingModule({
      imports: [
        LocationListPage,
        TranslocoTestingModule.forRoot({
          langs: { en, es: en },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: LocationFacade, useValue: { list } }],
    }).compileComponents();

    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:CREATE', scopes: [] } as never]);
    fixture = TestBed.createComponent(LocationListPage);
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function columns(): NxColDef<LocationListItem>[] {
    return (fixture.componentInstance as unknown as { columns(): NxColDef<LocationListItem>[] }).columns();
  }

  function cell(colId: string, data: LocationListItem = row): unknown {
    const column = columns().find((c) => (c.colId ?? c.field) === colId);
    return typeof column?.valueGetter === 'function' ? column.valueGetter({ data } as never) : undefined;
  }

  it('renders the list template: one h1, the record count, «New location» and no filter bar', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Locations');
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe('1 record');
    expect(root().querySelector('[data-testid="location-list-new"]')?.textContent).toContain('New location');
    expect(root().querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    // The API has no search or status filter.
    expect(root().querySelector('nx-filter-bar')).toBeNull();
  });

  it('shows the country by name in the active language, never the bare code', () => {
    fixture.detectChanges();
    expect(cell('countryCode')).toBe('Switzerland');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(cell('countryCode')).toBe('Suiza');
  });

  it('shows a dash for a missing code or city', () => {
    expect(cell('code', { ...row, code: null })).toBe('—');
    expect(cell('city', { ...row, city: null })).toBe('—');
  });

  it('only lets the user sort by the columns the API sorts on (name, city, status)', () => {
    expect(columns().filter((column) => column.sortable).map((column) => column.colId ?? column.field)).toEqual([
      'name',
      'city',
      'isActive',
    ]);
  });

  it('opens a row at its detail page', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { open(r: LocationListItem): void }).open(row);

    expect(navigate).toHaveBeenCalledWith(['/locations', row.id]);
  });
});
