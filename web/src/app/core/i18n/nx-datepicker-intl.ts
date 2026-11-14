import { Injectable, Provider, inject } from '@angular/core';
import { MatDatepickerIntl } from '@angular/material/datepicker';
import { TranslocoService } from '@jsverse/transloco';

/** `MatDatepickerIntl` label → key under `shared.datepicker`. */
const LABELS = {
  calendarLabel: 'calendar',
  openCalendarLabel: 'openCalendar',
  closeCalendarLabel: 'closeCalendar',
  prevMonthLabel: 'prevMonth',
  nextMonthLabel: 'nextMonth',
  prevYearLabel: 'prevYear',
  nextYearLabel: 'nextYear',
  prevMultiYearLabel: 'prevMultiYear',
  nextMultiYearLabel: 'nextMultiYear',
  switchToMonthViewLabel: 'switchToMonthView',
  switchToMultiYearViewLabel: 'switchToMultiYearView',
  startDateLabel: 'startDate',
  endDateLabel: 'endDate',
  comparisonDateLabel: 'comparisonDate',
} as const;

/**
 * `MatDatepickerIntl` backed by Transloco (`shared.datepicker.*`): the calendar's button labels
 * («Open calendar», «Previous month», …) follow the active language like the paginator's. Month
 * and day names come from the `DateAdapter` locale (`NxDateLocaleSync`).
 *
 * Provided by the (lazy) component that shows a datepicker — `providers: [provideNxDatepickerIntl()]`
 * — not in `provideNexusMaterialI18n()`: there it would pull the whole datepicker into the initial
 * bundle (+80 kB, over the 1 MB budget).
 */
@Injectable()
export class NxDatepickerIntl extends MatDatepickerIntl {
  private readonly transloco = inject(TranslocoService);

  constructor() {
    super();
    this.transloco.selectTranslation().subscribe(() => this.refresh());
  }

  override formatYearRangeLabel(start: string, end: string): string {
    return this.transloco.translate('shared.datepicker.yearRange', { start, end });
  }

  private refresh(): void {
    for (const [label, key] of Object.entries(LABELS) as [keyof typeof LABELS, string][]) {
      this[label] = this.transloco.translate(`shared.datepicker.${key}`);
    }
    this.changes.next();
  }
}

/** `providers: [provideNxDatepickerIntl()]` on a component that uses `mat-datepicker`. */
export function provideNxDatepickerIntl(): Provider {
  return { provide: MatDatepickerIntl, useClass: NxDatepickerIntl };
}
