import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { Page } from '../../../shared/ui/page.model';
import { CurrencyListPage } from './currency-list.page';
import { CurrenciesFacade, CurrencyToggleError } from './currencies.facade';

describe('CurrencyListPage', () => {
  function setup(toggleError: CurrencyToggleError | null = null) {
    const clearToggleError = vi.fn();
    const fetchPage = vi.fn(() =>
      of<Page<CurrencyListItem>>({
        items: [{ code: 'CHF', captionI18n: { de: 'Franken', en: 'Swiss Franc' }, minorUnits: 2, isActive: true }],
        page: 0,
        pageSize: 25,
        total: 1,
      }),
    );

    TestBed.configureTestingModule({
      imports: [CurrencyListPage],
      providers: [
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'en' },
        }),
        {
          provide: CurrenciesFacade,
          useValue: {
            fetchPage,
            toggleError: signal(toggleError),
            clearToggleError,
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(CurrencyListPage);
    fixture.detectChanges();
    return { fixture, clearToggleError, fetchPage };
  }

  it('renders the currencies the facade fetches, through nx-data-grid', async () => {
    const { fixture, fetchPage } = setup();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fetchPage).toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('CHF');
  });

  it('does not show the toggle-error banner when there is no error', async () => {
    const { fixture } = setup(null);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-currencies-toggle-error"]')).toBeNull();
  });

  it('shows a readable toggle error (code + detail) and dismisses it on click', async () => {
    const { fixture, clearToggleError } = setup({ code: 'CHF', message: 'CHF está en uso por LE-CH (activa).' });
    await fixture.whenStable();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="nx-currencies-toggle-error"]') as HTMLElement;
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('CHF');
    expect(banner.textContent).toContain('CHF está en uso por LE-CH (activa).');

    banner.querySelector('button')!.dispatchEvent(new Event('click'));
    expect(clearToggleError).toHaveBeenCalledOnce();
  });
});
