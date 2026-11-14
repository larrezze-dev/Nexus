import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxColDef } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { Page } from '../../../shared/ui/page.model';
import { BusinessUnitListPage, legalEntityCode, legalEntityName } from './business-unit-list.page';
import { BusinessUnitRow, BusinessUnitsFacade } from './business-units.facade';

const row: BusinessUnitRow = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-BCN-RD',
  captionI18n: { de: 'F+E Barcelona', es: 'I+D Barcelona' },
  isActive: true,
  legalEntity: { id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40', code: 'LE-SSB', legalName: 'SAPE smart bud S.L.', isActive: true },
};

const en = {
  org: {
    businessUnits: {
      title: 'Business units',
      new: 'New business unit',
      fields: { code: 'Code', caption: 'Name', legalEntityId: 'Legal entity', isActive: 'Status' },
      empty: 'No business units yet',
      emptyHint: 'Create the first one.',
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' } } },
};

describe('BusinessUnitListPage', () => {
  let fixture: ComponentFixture<BusinessUnitListPage>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn(() => of<Page<BusinessUnitRow>>({ items: [row], page: 0, pageSize: 25, total: 1 }));

    await TestBed.configureTestingModule({
      imports: [
        BusinessUnitListPage,
        TranslocoTestingModule.forRoot({
          langs: { en, es: en },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: BusinessUnitsFacade, useValue: { list } }],
    }).compileComponents();

    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:CREATE', scopes: [] } as never]);
    fixture = TestBed.createComponent(BusinessUnitListPage);
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function columns(): NxColDef<BusinessUnitRow>[] {
    return (fixture.componentInstance as unknown as { columns(): NxColDef<BusinessUnitRow>[] }).columns();
  }

  function cell(colId: string): unknown {
    const column = columns().find((c) => (c.colId ?? c.field) === colId);
    return typeof column?.valueGetter === 'function' ? column.valueGetter({ data: row } as never) : undefined;
  }

  it('renders the list template: one h1, the record count, «New business unit» and no filter bar', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Business units');
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe('1 record');
    expect(root().querySelector('[data-testid="bu-list-new"]')?.textContent).toContain('New business unit');
    expect(root().querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    // The API has no search or status filter.
    expect(root().querySelector('nx-filter-bar')).toBeNull();
  });

  it('shows the name in the active language and follows a language switch', () => {
    fixture.detectChanges();
    expect(cell('caption')).toBe('F+E Barcelona'); // no «en» caption → falls back to «de»

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(cell('caption')).toBe('I+D Barcelona');
  });

  it('shows the legal entity on two lines (legal name, code muted), never its id', () => {
    const params = columns().find((c) => c.colId === 'legalEntity')?.cellRendererParams as {
      primary(r: BusinessUnitRow): string;
      secondary(r: BusinessUnitRow): string | null;
    };
    expect(params.primary(row)).toBe('SAPE smart bud S.L.');
    expect(params.secondary(row)).toBe('LE-SSB');
    expect(legalEntityName({ legalEntity: null })).toBe('—');
    expect(legalEntityCode({ legalEntity: null })).toBeNull();
  });

  it('only lets the user sort by the columns the API sorts on (code, status)', () => {
    expect(columns().filter((column) => column.sortable).map((column) => column.colId ?? column.field)).toEqual([
      'code',
      'isActive',
    ]);
  });

  it('opens a row at its detail page', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { open(r: BusinessUnitRow): void }).open(row);

    expect(navigate).toHaveBeenCalledWith([row.id], { relativeTo: TestBed.inject(ActivatedRoute) });
  });
});
