import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { NxFormatService } from './nx-format.service';

describe('NxFormatService', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });
    return {
      service: TestBed.inject(NxFormatService),
      transloco: TestBed.inject(TranslocoService),
    };
  }

  it('formats a decimal-string amount using the active language (de)', () => {
    const { service, transloco } = setup();
    transloco.setActiveLang('de');
    expect(service.formatNumber('1250.5')).toBe('1.250,5');
  });

  it('formats the same amount differently for another active language (en)', () => {
    const { service, transloco } = setup();
    transloco.setActiveLang('en');
    expect(service.formatNumber('1250.5')).toBe('1,250.5');
  });

  it('formats a UTC instant for display in Europe/Zurich (en)', () => {
    const { service, transloco } = setup();
    transloco.setActiveLang('en');
    // 2026-06-18T10:00:00Z is CEST (UTC+2) in Zurich during summer: 12:00.
    const formatted = service.formatDate('2026-06-18T10:00:00Z');
    expect(formatted).toContain('12:00');
    expect(formatted).toContain('06/18/2026');
  });

  it('formats the same UTC instant for display in Europe/Zurich with another language (de)', () => {
    const { service, transloco } = setup();
    transloco.setActiveLang('de');
    const formatted = service.formatDate('2026-06-18T10:00:00Z');
    expect(formatted).toContain('12:00');
    expect(formatted).toContain('18.06.2026');
  });

  it('formats a medium date in Europe/Zurich with the regional locale of the language', () => {
    const { service, transloco } = setup();
    transloco.setActiveLang('de');
    // 2026-12-31T23:30:00Z is already 1 Jan 2027 in Zurich (UTC+1).
    expect(service.formatDateMedium('2026-12-31T23:30:00Z')).toBe('01.01.2027');
    transloco.setActiveLang('en');
    expect(service.formatDateMedium('2026-11-11T10:00:00Z')).toBe('11 Nov 2026');
  });

  it('returns an empty string for a missing or invalid date', () => {
    const { service } = setup();
    expect(service.formatDateMedium('not a date')).toBe('');
    expect(service.formatDateMedium('')).toBe('');
    expect(service.formatDateMedium(null)).toBe('');
    expect(service.formatDateMedium(undefined)).toBe('');
  });
});
