import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { CurrencyActionCellComponent, CurrencyActionCellParams } from './currency-action-cell.component';
import { CurrenciesFacade } from './currencies.facade';

function paramsFor(isActive: boolean, onChanged: () => void): CurrencyActionCellParams {
  return {
    data: { code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive },
    onChanged,
  } as unknown as CurrencyActionCellParams;
}

describe('CurrencyActionCellComponent', () => {
  function setup(toggleActive = vi.fn().mockResolvedValue(true)) {
    TestBed.configureTestingModule({
      imports: [
        CurrencyActionCellComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { org: { currencies: { actions: { activate: 'Activate', deactivate: 'Deactivate' } } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [{ provide: CurrenciesFacade, useValue: { toggleActive } }],
    });
    return { fixture: TestBed.createComponent(CurrencyActionCellComponent), toggleActive };
  }

  it('shows "Deactivate" for an active row and toggles to isActive=false on click, then calls onChanged', async () => {
    const { fixture, toggleActive } = setup();
    const onChanged = vi.fn();
    fixture.componentInstance.agInit(paramsFor(true, onChanged));
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    expect(button.textContent).toContain('Deactivate');

    button.click();
    await fixture.whenStable();

    expect(toggleActive).toHaveBeenCalledWith('CHF', false);
    expect(onChanged).toHaveBeenCalledOnce();
  });

  it('shows "Activate" for an inactive row', () => {
    const { fixture } = setup();
    fixture.componentInstance.agInit(paramsFor(false, vi.fn()));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('button')!.textContent).toContain('Activate');
  });

  it('refresh re-reads the row and returns true', () => {
    const { fixture } = setup();
    const refreshed = fixture.componentInstance.refresh(paramsFor(false, vi.fn()));
    expect(refreshed).toBe(true);
  });
});
