import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * Loads translation files from the static assets served by the app itself
 * (`public/assets/i18n/<lang>.json` at build time, served as `/assets/i18n/<lang>.json`),
 * per conventions-frontend.md §7 ("Textos de la UI en assets/i18n/*.json").
 */
@Injectable({ providedIn: 'root' })
export class NxTranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string) {
    return this.http.get<Translation>(`/assets/i18n/${lang}.json`);
  }
}
