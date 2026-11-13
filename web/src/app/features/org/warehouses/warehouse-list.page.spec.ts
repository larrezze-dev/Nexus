import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxColDef } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { Page } from '../../../shared/ui/page.model';
import { OrgNameCellParams } from '../shared/org-name-cell.component';
import { WarehouseListPage } from './warehouse-list.page';
import { WarehouseFacade } from './warehouse.facade';

const LE_ID = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40';
const BU_ID = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50';

const row: WarehouseListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c70',
  legalEntityId: LE_ID,
  businessUnitId: BU_ID,
  code: 'WH-ZH-01',
  captionI18n: { de: 'Lager Zürich', es: 'Almacén Zúrich' },
  isActive: true,
};

const en = {
  org: {
    warehouses: {
      title: 'Warehouses',
      new: 'New warehouse',
      fields: {
        code: 'Code',
        caption: 'Name',
        legalEntityId: 'Legal entity',
        businessUnitId: 'Business unit',
      },
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' } } },
};

describe('WarehouseListPage', () => {
  let fixture: ComponentFixture<WarehouseListPage>;
  let list: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    list = vi.fn(() =>
      of<Page<WarehouseListItem>>({ items: [row], page: 0, pageSize: 25, total: 1 }),
    );

    await TestBed.configureTestingModule({
      imports: [
        WarehouseListPage,
        TranslocoTestingModule.forRoot({
          langs: { en, es: en },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [
        provideRouter([]),
        {
          provide: WarehouseFacade,
          useValue: {
            list,
            searchLegalEntities: () =>
              of([
                { id: LE_ID, code: 'LE-CH', legalName: 'Nüssli Event & Rental AG', isActive: true },
              ]),
            searchBusinessUnits: () => () =>
              of([
                {
                  id: BU_ID,
                  legalEntityId: LE_ID,
                  code: 'BU-CH-EVENTS',
                  captionI18n: { de: 'Events CH' },
                  isActive: true,
                },
              ]),
          },
        },
      ],
    }).compileComponents();

    TestBed.inject(PermissionsStore).setPermissions([
      { code: 'ORG_STRUCTURE:CREATE', scopes: [] } as never,
    ]);
    fixture = TestBed.createComponent(WarehouseListPage);
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function columns(): NxColDef<WarehouseListItem>[] {
    return (
      fixture.componentInstance as unknown as { columns(): NxColDef<WarehouseListItem>[] }
    ).columns();
  }

  function column(colId: string): NxColDef<WarehouseListItem> | undefined {
    return columns().find((c) => (c.colId ?? c.field) === colId);
  }

  function nameCell(colId: string): { primary: string; secondary: string | null | undefined } {
    const params = column(colId)?.cellRendererParams as OrgNameCellParams<WarehouseListItem>;
    return { primary: params.primary(row), secondary: params.secondary?.(row) };
  }

  it('renders the list template: one h1, the record count, «New warehouse» and the four filters', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Warehouses');
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe(
      '1 record',
    );
    expect(root().querySelector('[data-testid="warehouse-list-new"]')?.textContent).toContain(
      'New warehouse',
    );
    for (const testId of ['q', 'legal-entity', 'business-unit', 'status']) {
      expect(
        root().querySelector(`nx-filter-bar [data-testid="warehouse-filter-${testId}"]`),
      ).toBeTruthy();
    }
  });

  it('shows legal entity and business unit by name with the code underneath, never the id', () => {
    fixture.detectChanges();

    expect(nameCell('legalEntity')).toEqual({
      primary: 'Nüssli Event & Rental AG',
      secondary: 'LE-CH',
    });
    expect(nameCell('businessUnit')).toEqual({ primary: 'Events CH', secondary: 'BU-CH-EVENTS' });
    expect(columns().map((c) => c.headerKey)).toEqual([
      'org.warehouses.fields.code',
      'org.warehouses.fields.caption',
      'org.warehouses.fields.legalEntityId',
      'org.warehouses.fields.businessUnitId',
      'org.warehouses.fields.isActive',
    ]);
  });

  it('shows the name in the active language and follows a language switch', () => {
    fixture.detectChanges();
    const caption = () => column('caption')?.valueGetter;
    const value = () => (caption() as (p: unknown) => string)({ data: row });
    expect(value()).toBe('Lager Zürich');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(value()).toBe('Almacén Zúrich');
  });

  it('sends the chosen filters to the API and clears them all', () => {
    fixture.detectChanges();
    const page = fixture.componentInstance as unknown as {
      filters(): Record<string, unknown>;
      onLegalEntityFilter(id: string): void;
      businessUnitId: { set(v: string): void };
      status: { set(v: string): void };
      clearFilters(): void;
    };
    page.onLegalEntityFilter(LE_ID);
    page.businessUnitId.set(BU_ID);
    page.status.set('inactive');

    expect(page.filters()).toEqual({
      q: undefined,
      legalEntityId: LE_ID,
      businessUnitId: BU_ID,
      isActive: false,
    });

    page.clearFilters();
    expect(page.filters()).toEqual({
      q: undefined,
      legalEntityId: undefined,
      businessUnitId: undefined,
      isActive: undefined,
    });
  });
});
