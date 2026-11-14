import { TestBed } from '@angular/core/testing';
import { MatDatepickerIntl } from '@angular/material/datepicker';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { NxDatepickerIntl } from './nx-datepicker-intl';

describe('NxDatepickerIntl', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({ config: { availableLangs: ['de', 'es'], defaultLang: 'de' } }),
        { provide: MatDatepickerIntl, useClass: NxDatepickerIntl },
      ],
    });
    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation(
      { shared: { datepicker: { openCalendar: 'Kalender öffnen', prevMonth: 'Vorheriger Monat', yearRange: '{{start}} bis {{end}}' } } },
      'de',
    );
    transloco.setTranslation(
      { shared: { datepicker: { openCalendar: 'Abrir calendario', prevMonth: 'Mes anterior', yearRange: '{{start}} a {{end}}' } } },
      'es',
    );
    transloco.setActiveLang('de');
    return { intl: TestBed.inject(MatDatepickerIntl), transloco };
  }

  it('reads the calendar labels in de', () => {
    const { intl } = setup();
    expect(intl.openCalendarLabel).toBe('Kalender öffnen');
    expect(intl.prevMonthLabel).toBe('Vorheriger Monat');
    expect(intl.formatYearRangeLabel('2024', '2047')).toBe('2024 bis 2047');
  });

  it('switches to es and emits changes so open calendars re-render', () => {
    const { intl, transloco } = setup();
    let emissions = 0;
    intl.changes.subscribe(() => emissions++);
    transloco.setActiveLang('es');
    expect(intl.openCalendarLabel).toBe('Abrir calendario');
    expect(intl.formatYearRangeLabel('2024', '2047')).toBe('2024 a 2047');
    expect(emissions).toBeGreaterThan(0);
  });
});
