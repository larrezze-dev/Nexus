import { Injectable, inject } from '@angular/core';
import { DateAdapter } from '@angular/material/core';
import { TranslocoService } from '@jsverse/transloco';
import { DEFAULT_LANGUAGE, SupportedLanguage, normalizeToSupportedLanguage } from './supported-languages';

/** Regional locale used for dates per UI language (dd.mm.yyyy for de, dd/mm/yyyy for es, ...). */
export const DATE_LOCALES: Readonly<Record<SupportedLanguage, string>> = {
  de: 'de-CH',
  en: 'en-GB',
  fr: 'fr-CH',
  it: 'it-CH',
  es: 'es-ES',
};

export function dateLocaleFor(language: string | null | undefined): string {
  return DATE_LOCALES[normalizeToSupportedLanguage(language) ?? DEFAULT_LANGUAGE];
}

/** Keeps the Material `DateAdapter` locale in step with the active UI language. */
@Injectable({ providedIn: 'root' })
export class NxDateLocaleSync {
  private readonly adapter = inject<DateAdapter<unknown>>(DateAdapter);

  constructor() {
    inject(TranslocoService).langChanges$.subscribe((lang) => this.adapter.setLocale(dateLocaleFor(lang)));
  }
}
