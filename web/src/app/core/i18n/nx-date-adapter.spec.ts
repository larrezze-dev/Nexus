import { TestBed } from '@angular/core/testing';
import { DateAdapter } from '@angular/material/core';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { NxDateAdapter } from './nx-date-adapter';
import { NxDateLocaleSync } from './nx-date-locale';
import { provideNexusMaterialI18n } from './provide-nexus-material-i18n';

describe('NxDateAdapter parse', () => {
  let adapter: DateAdapter<Date>;
  const ymd = (d: Date | null) => (d && adapter.isValid(d) ? [d.getFullYear(), d.getMonth() + 1, d.getDate()] : null);

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
        ...provideNexusMaterialI18n(),
      ],
    });
    TestBed.inject(NxDateLocaleSync);
    adapter = TestBed.inject<DateAdapter<Date>>(DateAdapter);
  });

  it('is the app DateAdapter', () => {
    expect(adapter).toBeInstanceOf(NxDateAdapter);
  });

  it.each([
    ['de', '14.11.2026', [2026, 11, 14]],
    ['de', '1.2.2026', [2026, 2, 1]],
    ['es', '01/11/2026', [2026, 11, 1]],
    ['fr', '01/11/2026', [2026, 11, 1]],
    ['it', '14-11-2026', [2026, 11, 14]],
    ['en', '01/11/2026', [2026, 11, 1]],
    ['es', '14/11/26', [2026, 11, 14]],
    ['de', '14.11.26', [2026, 11, 14]],
    ['es', '2026-11-14', [2026, 11, 14]],
  ])('%s: «%s» is read day-first', (lang, text, expected) => {
    TestBed.inject(TranslocoService).setActiveLang(lang);
    expect(ymd(adapter.parse(text, null))).toEqual(expected);
  });

  it.each(['31.02.2026', '32/01/2026', '01/13/2026', '2026', 'abc', '14.11.', '1/1/20266'])(
    '«%s» is invalid',
    (text) => {
      expect(ymd(adapter.parse(text, null))).toBeNull();
    },
  );

  it('empty text is null (no value), not invalid', () => {
    expect(adapter.parse('  ', null)).toBeNull();
  });
});

describe('NxDateAdapter parse without Material dev-mode checks', () => {
  /** Mimics production, where `createDate` does not throw on overflowing days/months. */
  class ProductionLikeAdapter extends NxDateAdapter {
    override createDate(year: number, month: number, date: number): Date {
      return new Date(year, month, date);
    }
  }

  it.each(['31.02.2026', '30.02.2026', '29.02.2026', '32.01.2026', '01.13.2026', '00.05.2026', '2026-02-31', '2026-00-10'])(
    '«%s» is invalid even when createDate does not throw',
    (text) => {
      TestBed.configureTestingModule({});
      const adapter = TestBed.runInInjectionContext(() => new ProductionLikeAdapter());
      expect(adapter.isValid(adapter.parse(text, null) as Date)).toBe(false);
    },
  );

  it('still accepts real dates, including 29 February of a leap year', () => {
    TestBed.configureTestingModule({});
    const adapter = TestBed.runInInjectionContext(() => new ProductionLikeAdapter());
    const date = adapter.parse('29.02.2028', null) as Date;
    expect([date.getFullYear(), date.getMonth() + 1, date.getDate()]).toEqual([2028, 2, 29]);
  });
});
