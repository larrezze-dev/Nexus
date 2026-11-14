import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it, vi } from 'vitest';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { CurrenciesFacade } from './currencies.facade';
import { CurrencyCreatePage } from './currency-create.page';

const translations = {
  en: {
    org: { currencies: { newTitle: 'New currency', createSubtitle: 'Subtitle', created: 'Currency created' } },
    errors: { validation: 'Fix the fields' },
  },
};

const value = { code: 'SEK', captionI18n: { de: 'Schwedische Krone' }, minorUnits: 2 };

async function setup(create: ReturnType<typeof vi.fn>) {
  const snackBar = { open: vi.fn() };
  await TestBed.configureTestingModule({
    imports: [
      CurrencyCreatePage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      { provide: CurrenciesFacade, useValue: { create } },
      { provide: MatSnackBar, useValue: snackBar },
      { provide: NxI18nService, useValue: { translateErrorCode: (code?: string) => `error:${code}` } },
      {
        provide: NxBreadcrumbService,
        useValue: { claimForPage: () => () => undefined, trail: () => [], setCurrentLabel: vi.fn() },
      },
    ],
  }).compileComponents();
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture: ComponentFixture<CurrencyCreatePage> = TestBed.createComponent(CurrencyCreatePage);
  fixture.detectChanges();
  return { fixture, navigate, snackBar };
}

type Internals = {
  onSave(v: typeof value): Promise<void>;
  fieldErrors(): Record<string, string[]>;
  saveError(): string | null;
};
const internals = (fixture: ComponentFixture<CurrencyCreatePage>) => fixture.componentInstance as unknown as Internals;

describe('CurrencyCreatePage', () => {
  it('creates the currency and opens its detail page', async () => {
    const create = vi.fn().mockResolvedValue({ ...value, isActive: true });
    const { fixture, navigate, snackBar } = await setup(create);

    await internals(fixture).onSave(value);

    expect(create).toHaveBeenCalledWith(value);
    expect(snackBar.open).toHaveBeenCalledWith('Currency created', undefined, { duration: 4000 });
    expect(navigate).toHaveBeenCalledWith(['/org/currencies', 'SEK']);
  });

  it('a 409 CONFLICT marks the code field with the translated message and does not navigate', async () => {
    const create = vi.fn().mockRejectedValue({
      status: 409,
      error: { type: 'https://nexus.nussli.ch/problems/conflict', code: 'CONFLICT', status: 409 },
    });
    const { fixture, navigate } = await setup(create);

    await internals(fixture).onSave(value);

    expect(internals(fixture).fieldErrors()).toEqual({ code: ['org.currencies.errors.duplicateCode'] });
    expect(internals(fixture).saveError()).toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a 400 shows the generic «invalid value» per field, never the English server text', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const create = vi.fn().mockRejectedValue({
      status: 400,
      error: { errors: { minorUnits: ['must be between 0 and 4'] } },
    });
    const { fixture } = await setup(create);

    await internals(fixture).onSave(value);

    expect(internals(fixture).fieldErrors()).toEqual({ minorUnits: ['shared.form.invalidValue'] });
    expect(internals(fixture).saveError()).toBe('Fix the fields');
    warn.mockRestore();
  });

  it('a 422 shows the translated business-rule code', async () => {
    const create = vi.fn().mockRejectedValue({ status: 422, error: { code: 'ORG-R7' } });
    const { fixture } = await setup(create);

    await internals(fixture).onSave(value);

    expect(internals(fixture).saveError()).toBe('error:ORG-R7');
  });
});
