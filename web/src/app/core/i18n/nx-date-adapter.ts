import { Injectable } from '@angular/core';
import { NativeDateAdapter } from '@angular/material/core';

const DAY_FIRST = /^(\d{1,2})\s*[./-]\s*(\d{1,2})\s*[./-]\s*(\d{4}|\d{2})$/;
const ISO = /^(\d{4})-(\d{1,2})-(\d{1,2})$/;

/**
 * `NativeDateAdapter` whose `parse()` reads typed dates day-first, as every UI locale of the app displays them
 * (`de-CH` dd.MM.yyyy, `fr-CH`/`it-CH`/`es-ES`/`en-GB` dd/MM/yyyy). The native adapter delegates to
 * `Date.parse`, which is US-style month-first («01/11/2026» → 11 Jan) and rejects «14.11.2026».
 *
 * Accepts `.`, `/` and `-` as separators in any language, 2- or 4-digit years (2-digit = 20yy) and ISO
 * `yyyy-MM-dd`. Impossible dates (31.02.2026) are invalid. English uses `en-GB` (see `DATE_LOCALES`), so it is
 * day-first too; switch both together if `en-US` is ever adopted.
 */
@Injectable()
export class NxDateAdapter extends NativeDateAdapter {
  override parse(value: unknown, parseFormat?: unknown): Date | null {
    if (typeof value !== 'string') {
      return super.parse(value, parseFormat);
    }
    const text = value.trim();
    if (!text) {
      return null;
    }
    const iso = ISO.exec(text);
    if (iso) {
      return this.build(Number(iso[1]), Number(iso[2]), Number(iso[3]));
    }
    const match = DAY_FIRST.exec(text);
    if (!match) {
      return this.invalid();
    }
    const year = match[3].length === 2 ? 2000 + Number(match[3]) : Number(match[3]);
    return this.build(year, Number(match[2]), Number(match[1]));
  }

  private build(year: number, month: number, day: number): Date {
    try {
      return this.createDate(year, month - 1, day); // throws on month/day out of range
    } catch {
      return this.invalid();
    }
  }
}
