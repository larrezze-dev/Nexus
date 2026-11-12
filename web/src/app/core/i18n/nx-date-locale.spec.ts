import { TestBed } from '@angular/core/testing';
import { DateAdapter, MAT_DATE_LOCALE } from '@angular/material/core';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { NxDateLocaleSync, dateLocaleFor } from './nx-date-locale';
import { NxI18nService } from './nx-i18n.service';
import { provideNexusMaterialI18n } from './provide-nexus-material-i18n';

describe('date locale', () => {
  const originalLanguage = navigator.language;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
        ...provideNexusMaterialI18n(),
      ],
    });
  });

  it('maps each language to its regional locale', () => {
    expect(['de', 'en', 'fr', 'it', 'es'].map(dateLocaleFor)).toEqual(['de-CH', 'en-GB', 'fr-CH', 'it-CH', 'es-ES']);
    expect(dateLocaleFor('pt')).toBe('de-CH');
  });

  it('provides MAT_DATE_LOCALE for the language active at start', () => {
    // App initializers run on first TestBed use, so the browser language must be stubbed before it.
    Object.defineProperty(navigator, 'language', { value: 'es-ES', configurable: true });
    expect(TestBed.inject(MAT_DATE_LOCALE)).toBe('es-ES');
    Object.defineProperty(navigator, 'language', { value: originalLanguage, configurable: true });
  });

  it('re-sets the DateAdapter locale on every language change (de dd.mm.yyyy, es dd/mm/yyyy)', () => {
    TestBed.inject(NxI18nService);
    TestBed.inject(NxDateLocaleSync);
    const adapter = TestBed.inject<DateAdapter<Date>>(DateAdapter);
    const transloco = TestBed.inject(TranslocoService);
    const date = new Date(2026, 10, 5);
    const format = () => adapter.format(date, { year: 'numeric', month: '2-digit', day: '2-digit' });

    transloco.setActiveLang('de');
    expect((adapter as unknown as { locale: string }).locale).toBe('de-CH');
    expect(format()).toBe('05.11.2026');

    transloco.setActiveLang('es');
    expect((adapter as unknown as { locale: string }).locale).toBe('es-ES');
    expect(format()).toBe('05/11/2026');

    transloco.setActiveLang('en');
    expect((adapter as unknown as { locale: string }).locale).toBe('en-GB');
  });
});
