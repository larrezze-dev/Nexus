import { Injectable, inject } from '@angular/core';
import { NxI18nService } from './nx-i18n.service';

/** IANA zone used to display dates/times across NEXUS (conventions-frontend.md §7). */
export const DISPLAY_TIME_ZONE = 'Europe/Zurich';

/**
 * Formatting helpers for numbers/amounts and dates, per conventions-frontend.md §7:
 * amounts and quantities travel as decimal strings and are never computed on the client;
 * they are only formatted for display, using the active UI language. Instants travel as
 * UTC ISO-8601 and are displayed in `Europe/Zurich`.
 */
@Injectable({ providedIn: 'root' })
export class NxFormatService {
  private readonly i18n = inject(NxI18nService);

  /** Formats a decimal-string or number amount/quantity using `Intl.NumberFormat` for the active language. */
  formatNumber(value: string | number, options?: Intl.NumberFormatOptions): string {
    const numeric = typeof value === 'string' ? Number(value) : value;
    if (Number.isNaN(numeric)) {
      return String(value);
    }
    return new Intl.NumberFormat(this.i18n.getActiveLanguage(), options).format(numeric);
  }

  /** Formats a UTC ISO-8601 instant/date for display in `Europe/Zurich`, using the active language. */
  formatDate(isoUtc: string, options?: Intl.DateTimeFormatOptions): string {
    const date = new Date(isoUtc);
    const formatOptions: Intl.DateTimeFormatOptions = {
      timeZone: DISPLAY_TIME_ZONE,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      ...options,
    };
    return new Intl.DateTimeFormat(this.i18n.getActiveLanguage(), formatOptions).format(date);
  }
}
