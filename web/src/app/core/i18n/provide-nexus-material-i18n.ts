import { EnvironmentProviders, Provider, inject, provideAppInitializer } from '@angular/core';
import { DateAdapter, MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { NxDateAdapter } from './nx-date-adapter';
import { NxDateLocaleSync, dateLocaleFor } from './nx-date-locale';
import { NxI18nService } from './nx-i18n.service';
import { NxPaginatorIntl } from './nx-paginator-intl';

/**
 * Angular Material i18n: translated paginator labels and a native date adapter whose locale
 * follows the active language. Add once to `app.config.ts`, after `provideNexusI18n()`.
 */
export function provideNexusMaterialI18n(): Array<Provider | EnvironmentProviders> {
  return [
    { provide: MatPaginatorIntl, useClass: NxPaginatorIntl },
    provideNativeDateAdapter(),
    { provide: DateAdapter, useClass: NxDateAdapter }, // day-first parsing of typed dates
    { provide: MAT_DATE_LOCALE, useFactory: () => dateLocaleFor(inject(NxI18nService).getActiveLanguage()) },
    provideAppInitializer(() => {
      inject(NxDateLocaleSync);
    }),
  ];
}
