import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { ICellRendererParams } from 'ag-grid-community';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { CurrencyStatusCellComponent } from './currency-status-cell.component';

function paramsFor(isActive: boolean): ICellRendererParams<CurrencyListItem> {
  return { data: { code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive } } as unknown as ICellRendererParams<CurrencyListItem>;
}

describe('CurrencyStatusCellComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        CurrencyStatusCellComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { shared: { status: { active: 'Active', inactive: 'Inactive' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }).compileComponents();
  });

  it('shows "Active" when the row is active and "Inactive" after refresh toggles it off', () => {
    const fixture = TestBed.createComponent(CurrencyStatusCellComponent);
    fixture.componentInstance.agInit(paramsFor(true));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Active');

    const refreshed = fixture.componentInstance.refresh(paramsFor(false));
    fixture.detectChanges();

    expect(refreshed).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Inactive');
  });
});
