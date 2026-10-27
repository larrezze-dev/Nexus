import { Injectable, Signal, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import {
  DEFAULT_LANGUAGE,
  SupportedLanguage,
  normalizeToSupportedLanguage,
} from './supported-languages';

/**
 * Resolves and holds the active UI language.
 *
 * Initial-language chain (ficha 0.5, track F point 3):
 *   1. `preferredLanguage` from `GET /me` — not available yet in this track.
 *      `setInitialLanguage()` is the hook the integration step (F5) calls once it has that value.
 *   2. The browser's language (`navigator.language`), if it is one of `SUPPORTED_LANGUAGES`.
 *   3. `de` as the final default.
 *
 * At construction time (forced eagerly by `provideNexusI18n()` via `provideAppInitializer`)
 * only steps 2–3 are available, so the active language is set from the browser immediately;
 * `setInitialLanguage(preferredLanguage)` lets a caller re-run the full chain once step 1's
 * value is known, without a page reload.
 */
@Injectable({ providedIn: 'root' })
export class NxI18nService {
  private readonly transloco = inject(TranslocoService);

  /** Signal that reflects the currently active language (re-exported from Transloco for convenience). */
  readonly activeLang: Signal<string> = this.transloco.activeLang;

  constructor() {
    this.transloco.setActiveLang(this.resolveBrowserLanguage() ?? DEFAULT_LANGUAGE);
  }

  /**
   * Re-resolves the initial language now that `preferredLanguage` (from `/me`) is known.
   * Pass `null`/`undefined` when the user has no stored preference yet, so the chain
   * falls through to the browser language and then to `de`.
   */
  setInitialLanguage(preferredLanguage: string | null | undefined): void {
    const resolved =
      normalizeToSupportedLanguage(preferredLanguage) ??
      this.resolveBrowserLanguage() ??
      DEFAULT_LANGUAGE;
    this.transloco.setActiveLang(resolved);
  }

  /** Sets the active language explicitly, e.g. from the language selector. */
  setLanguage(lang: SupportedLanguage): void {
    this.transloco.setActiveLang(lang);
  }

  getActiveLanguage(): string {
    return this.transloco.getActiveLang();
  }

  /**
   * Translates a business-rule error code (422 `Problem.code`, e.g. `STK-R5`) using the
   * `errors.<code>` key, falling back to `errors.generic` when no specific translation exists
   * (conventions-api.md §6 / ficha 0.5 point 2).
   */
  translateErrorCode(code: string | null | undefined): string {
    if (!code) {
      return this.transloco.translate('errors.generic');
    }
    const key = `errors.${code}`;
    const translated = this.transloco.translate(key);
    return translated === key ? this.transloco.translate('errors.generic') : translated;
  }

  private resolveBrowserLanguage(): SupportedLanguage | null {
    const browserLanguage = typeof navigator !== 'undefined' ? navigator.language : null;
    return normalizeToSupportedLanguage(browserLanguage);
  }
}
