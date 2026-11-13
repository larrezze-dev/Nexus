import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { NxColDef } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { Page } from '../../../shared/ui/page.model';
import { CurrencyListPage } from './currency-list.page';
import { CurrenciesFacade } from './currencies.facade';

const row: CurrencyListItem = {
  code: 'CHF',
  captionI18n: { de: 'Schweizer Franken', en: 'Swiss franc', es: 'Franco suizo' },
  minorUnits: 2,
  isActive: true,
};

const en = {
  org: {
    currencies: {
      title: 'Currencies',
      empty: 'No currencies available.',
      fields: { code: 'Code', caption: 'Name', minorUnits: 'Decimal places', isActive: 'Status' },
    },
  },
  shared: { layout: { pageHeader: { count: '{{count}} records', countOne: '1 record' } } },
};
const es = {
  ...en,
  org: {
    currencies: {
      ...en.org.currencies,
      title: 'Monedas',
      fields: { ...en.org.currencies.fields, code: 'Código' },
    },
  },
};

describe('CurrencyListPage', () => {
  let fixture: ComponentFixture<CurrencyListPage>;
  let fetchPage: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    fetchPage = vi.fn(() =>
      of<Page<CurrencyListItem>>({ items: [row], page: 0, pageSize: 25, total: 1 }),
    );

    await TestBed.configureTestingModule({
      imports: [
        CurrencyListPage,
        TranslocoTestingModule.forRoot({
          langs: { en, es },
          translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideRouter([]), { provide: CurrenciesFacade, useValue: { fetchPage } }],
    }).compileComponents();

    fixture = TestBed.createComponent(CurrencyListPage);
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function columns(): NxColDef<CurrencyListItem>[] {
    return (
      fixture.componentInstance as unknown as { columns(): NxColDef<CurrencyListItem>[] }
    ).columns();
  }

  function cell(colId: string, data: CurrencyListItem = row): unknown {
    const column = columns().find((c) => (c.colId ?? c.field) === colId);
    return typeof column?.valueGetter === 'function'
      ? column.valueGetter({ data } as never)
      : undefined;
  }

  it('renders the list template: one h1, breadcrumb, record count, no «New» and no filter bar', () => {
    fixture.detectChanges();

    expect(root().querySelectorAll('h1').length).toBe(1);
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Currencies');
    expect(root().querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    expect(root().querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe(
      '1 record',
    );
    // No create endpoint and no filters in the contract.
    expect(root().querySelector('nx-filter-bar')).toBeNull();
    expect(root().querySelector('.nx-create--fab, .nx-create--header')).toBeNull();
    // No native buttons left in the page (the old inline toggle is gone).
    expect(
      root().querySelector(
        'button:not([mat-button]):not([mat-stroked-button]):not([mat-flat-button])' +
          ':not([mat-icon-button]):not(.nx-data-grid__card):not(.mat-mdc-paginator-navigation-previous)' +
          ':not(.mat-mdc-paginator-navigation-next):not(.mat-mdc-paginator-navigation-first)' +
          ':not(.mat-mdc-paginator-navigation-last)',
      ),
    ).toBeNull();
  });

  it('declares translatable headers (headerKey) for code, name, decimal places and status', () => {
    expect(columns().map((c) => c.headerKey)).toEqual([
      'org.currencies.fields.code',
      'org.currencies.fields.caption',
      'org.currencies.fields.minorUnits',
      'org.currencies.fields.isActive',
    ]);
  });

  it('shows the currency name in the active language', () => {
    fixture.detectChanges();
    expect(cell('caption')).toBe('Swiss franc');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(cell('caption')).toBe('Franco suizo');

    // Falls back to German when the active language has no caption.
    expect(cell('caption', { ...row, captionI18n: { de: 'Schweizer Franken' } })).toBe(
      'Schweizer Franken',
    );
  });

  it('uses name / code / status on the mobile cards', () => {
    expect((fixture.componentInstance as unknown as { cardFields: unknown }).cardFields).toEqual({
      title: 'caption',
      subtitle: 'code',
      status: 'isActive',
    });
  });

  it('only lets the user sort by the columns the API sorts on (code, decimal places, status)', () => {
    expect(
      columns()
        .filter((c) => c.sortable)
        .map((c) => c.colId ?? c.field),
    ).toEqual(['code', 'minorUnits', 'isActive']);
  });

  it('opens a row at its detail page', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();

    (fixture.componentInstance as unknown as { open(r: CurrencyListItem): void }).open(row);

    expect(navigate).toHaveBeenCalledWith(['/org/currencies', 'CHF']);
  });
});
