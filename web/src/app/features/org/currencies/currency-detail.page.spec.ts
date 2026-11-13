import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it, vi } from 'vitest';
import { Currency } from '../../../api/platform/models/currency';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { CurrencyDetailPage } from './currency-detail.page';
import { CurrenciesFacade, CurrencyToggleError } from './currencies.facade';

const chf: Currency = {
  code: 'CHF',
  captionI18n: { de: 'Schweizer Franken', en: 'Swiss franc', es: 'Franco suizo' },
  minorUnits: 2,
  isActive: true,
};

const currencies = {
  title: 'Currencies',
  loading: 'Loading…',
  fields: { code: 'Code', caption: 'Name', minorUnits: 'Decimal places', isActive: 'Status' },
  sections: { currency: 'Currency' },
  hints: { fixed: 'Predefined.', minorUnits: 'Decimals.' },
  activate: 'Activate',
  deactivate: 'Deactivate',
  deactivateDialog: { title: 'Deactivate the currency?', message: '«{{name}}» ({{code}}) …' },
  activated: 'Currency activated',
  deactivated: 'Currency deactivated',
  errors: { inUse: '«{{name}}» is in use.' },
};

const translations = {
  en: { org: { currencies }, common: { retry: 'Retry' } },
  es: { org: { currencies: { ...currencies, title: 'Monedas' } }, common: { retry: 'Reintentar' } },
};

interface Setup {
  readonly currency?: Currency | null;
  readonly permissions?: string[];
  readonly confirmed?: boolean;
  readonly toggleOk?: boolean;
  readonly toggleError?: CurrencyToggleError | null;
}

async function setupPage(options: Setup = {}) {
  const current = signal<Currency | null>(options.currency === undefined ? chf : options.currency);
  const toggleError = signal<CurrencyToggleError | null>(null);
  const facade = {
    get: vi.fn(() => {
      const value = current();
      return value ? Promise.resolve(value) : Promise.reject(new Error('load failed'));
    }),
    toggleActive: vi.fn(async (_code: string, isActive: boolean) => {
      if (options.toggleOk === false) {
        toggleError.set(options.toggleError ?? null);
        return false;
      }
      current.update((value) => (value ? { ...value, isActive } : value));
      return true;
    }),
    toggleError,
    clearToggleError: vi.fn(() => toggleError.set(null)),
  };
  const confirm = { confirm: vi.fn().mockResolvedValue(options.confirmed ?? true) };
  const snackBar = { open: vi.fn() };
  const breadcrumb = {
    setCurrentLabel: vi.fn(),
    claimForPage: () => () => undefined,
    trail: signal([]),
  };

  await TestBed.configureTestingModule({
    imports: [
      CurrencyDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: CurrenciesFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap({ code: 'CHF' }) } },
      },
      { provide: NxBreadcrumbService, useValue: breadcrumb },
      { provide: NxConfirm, useValue: confirm },
      { provide: MatSnackBar, useValue: snackBar },
    ],
  }).compileComponents();

  TestBed.inject(PermissionsStore).setPermissions(
    (options.permissions ?? ['CURRENCY:READ', 'CURRENCY:UPDATE']).map(
      (code) => ({ code, scopes: [] }) as never,
    ),
  );
  vi.spyOn(TestBed.inject(NxI18nService), 'translateErrorCode').mockImplementation(
    (code) => `error:${code}`,
  );

  const fixture: ComponentFixture<CurrencyDetailPage> = TestBed.createComponent(CurrencyDetailPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, facade, confirm, snackBar, breadcrumb };
}

function root(fixture: ComponentFixture<CurrencyDetailPage>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function byTestId(fixture: ComponentFixture<CurrencyDetailPage>, id: string): HTMLElement | null {
  return root(fixture).querySelector(`[data-testid="${id}"]`);
}

describe('CurrencyDetailPage', () => {
  it('shows the name in the active language as title, the code as subtitle and the status', async () => {
    const { fixture, breadcrumb } = await setupPage();

    expect(root(fixture).querySelectorAll('h1').length).toBe(1);
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('Swiss franc');
    expect(root(fixture).querySelector('.nx-page-header__subtitle')?.textContent?.trim()).toBe(
      'CHF',
    );
    expect(root(fixture).querySelector('nx-status-badge')).toBeTruthy();
    expect(breadcrumb.setCurrentLabel).toHaveBeenLastCalledWith('Swiss franc');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('Franco suizo');
    expect(breadcrumb.setCurrentLabel).toHaveBeenLastCalledWith('Franco suizo');
  });

  it('shows every field read-only, without tabs and without a save bar', async () => {
    const { fixture } = await setupPage();

    const inputs = Array.from(root(fixture).querySelectorAll('input')) as HTMLInputElement[];
    expect(inputs.map((input) => input.value)).toEqual(['CHF', 'Swiss franc', '2']);
    expect(inputs.every((input) => input.disabled)).toBe(true);
    expect(root(fixture).querySelector('mat-tab-group')).toBeNull();
    expect(root(fixture).querySelector('nx-action-bar')).toBeNull();
    expect(root(fixture).querySelector('[mat-flat-button]')).toBeNull();
  });

  it('deactivates after the destructive confirmation, then reloads and notifies', async () => {
    const { fixture, facade, confirm, snackBar } = await setupPage();

    byTestId(fixture, 'currency-detail-deactivate')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(confirm.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        destructive: true,
        message: '«Swiss franc» (CHF) …',
        confirmLabel: 'Deactivate',
      }),
    );
    expect(facade.toggleActive).toHaveBeenCalledWith('CHF', false);
    expect(snackBar.open).toHaveBeenCalledWith('Currency deactivated', undefined, {
      duration: 4000,
    });
    expect(facade.get).toHaveBeenCalledTimes(2);
    expect(byTestId(fixture, 'currency-detail-activate')).toBeTruthy();
  });

  it('does nothing when the confirmation is cancelled', async () => {
    const { fixture, facade } = await setupPage({ confirmed: false });

    byTestId(fixture, 'currency-detail-deactivate')!.click();
    await fixture.whenStable();

    expect(facade.toggleActive).not.toHaveBeenCalled();
  });

  it('activates an inactive currency without a confirmation', async () => {
    const { fixture, facade, confirm } = await setupPage({ currency: { ...chf, isActive: false } });

    expect(byTestId(fixture, 'currency-detail-deactivate')).toBeNull();
    byTestId(fixture, 'currency-detail-activate')!.click();
    await fixture.whenStable();

    expect(confirm.confirm).not.toHaveBeenCalled();
    expect(facade.toggleActive).toHaveBeenCalledWith('CHF', true);
  });

  it('explains ORG-R7 (currency in use) in the user language when the deactivation is refused', async () => {
    const { fixture } = await setupPage({
      toggleOk: false,
      toggleError: { code: 'CHF', message: 'CHF is used by LE-CH', problemCode: 'ORG-R7' },
    });

    byTestId(fixture, 'currency-detail-deactivate')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = byTestId(fixture, 'currency-detail-error');
    expect(alert?.getAttribute('role')).toBe('alert');
    expect(alert?.querySelector('.nx-alert__text')?.textContent?.trim()).toBe(
      '«Swiss franc» is in use.',
    );
  });

  it('shows any other refusal with the translated error code', async () => {
    const { fixture } = await setupPage({
      toggleOk: false,
      toggleError: { code: 'CHF', message: 'x' },
    });

    byTestId(fixture, 'currency-detail-deactivate')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(
      byTestId(fixture, 'currency-detail-error')
        ?.querySelector('.nx-alert__text')
        ?.textContent?.trim(),
    ).toBe('error:undefined');
  });

  it('offers no action without CURRENCY:UPDATE', async () => {
    const { fixture } = await setupPage({ permissions: ['CURRENCY:READ'] });

    expect(byTestId(fixture, 'currency-detail-deactivate')).toBeNull();
    expect(byTestId(fixture, 'currency-detail-activate')).toBeNull();
  });

  it('shows the error state with «Retry» when the load fails', async () => {
    const { fixture, facade } = await setupPage({ currency: null });

    expect(byTestId(fixture, 'currency-detail-load-error')).toBeTruthy();
    root(fixture)
      .querySelector<HTMLButtonElement>('[data-testid="currency-detail-load-error"] button')!
      .click();
    expect(facade.get).toHaveBeenCalledTimes(2);
  });
});
