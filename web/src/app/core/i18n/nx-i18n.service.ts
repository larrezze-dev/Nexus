import { DOCUMENT } from '@angular/common';
import { Injectable, InjectionToken, Signal, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import {
  DEFAULT_LANGUAGE,
  SupportedLanguage,
  isSupportedLanguage,
  normalizeToSupportedLanguage,
} from './supported-languages';

/** `localStorage` key holding the language the user explicitly picked in the selector. */
export const LANGUAGE_STORAGE_KEY = 'nx.lang';

/**
 * Hook called whenever the user explicitly picks a language. The default is a no-op; a later
 * step can provide a saver that persists it server-side (`PATCH /v1/me`) without touching
 * `NxI18nService`.
 */
export type LanguagePreferenceSaver = (language: SupportedLanguage) => void | Promise<void>;

export const NX_LANGUAGE_PREFERENCE_SAVER = new InjectionToken<LanguagePreferenceSaver>(
  'NX_LANGUAGE_PREFERENCE_SAVER',
  { providedIn: 'root', factory: () => () => undefined },
);

/**
 * Resolves and holds the active UI language.
 *
 * Resolution order (highest priority first):
 *   1. The language the user explicitly chose (`localStorage['nx.lang']`).
 *   2. `preferredLanguage` from `GET /v1/me`, fed in through `setInitialLanguage()`.
 *   3. The browser's language (`navigator.language`), if supported.
 *   4. `de`.
 *
 * At construction only 1, 3 and 4 are known; `setInitialLanguage(preferredLanguage)` re-runs
 * the whole chain once `/me` is known. `<html lang>` always mirrors the active language.
 */
@Injectable({ providedIn: 'root' })
export class NxI18nService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly saver = inject(NX_LANGUAGE_PREFERENCE_SAVER);

  /** Signal that reflects the currently active language (re-exported from Transloco for convenience). */
  readonly activeLang: Signal<string> = this.transloco.activeLang;

  constructor() {
    this.transloco.langChanges$.subscribe((lang) => {
      this.document.documentElement.lang = lang;
    });
    this.transloco.setActiveLang(this.resolve(null));
  }

  /**
   * Re-resolves the initial language now that `preferredLanguage` (from `/me`) is known.
   * An explicit choice stored in `localStorage` still wins over it.
   */
  setInitialLanguage(preferredLanguage: string | null | undefined): void {
    this.transloco.setActiveLang(this.resolve(preferredLanguage));
  }

  /** Sets the active language from an explicit user choice: remembered locally and handed to the saver hook. */
  setLanguage(lang: SupportedLanguage): void {
    this.transloco.setActiveLang(lang);
    this.writeStoredLanguage(lang);
    try {
      void Promise.resolve(this.saver(lang)).catch(() => undefined);
    } catch {
      // The preference is a convenience: a failing saver never blocks the language switch.
    }
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

  private resolve(preferredLanguage: string | null | undefined): SupportedLanguage {
    return (
      this.readStoredLanguage() ??
      normalizeToSupportedLanguage(preferredLanguage) ??
      this.resolveBrowserLanguage() ??
      DEFAULT_LANGUAGE
    );
  }

  private resolveBrowserLanguage(): SupportedLanguage | null {
    const browserLanguage = typeof navigator !== 'undefined' ? navigator.language : null;
    return normalizeToSupportedLanguage(browserLanguage);
  }

  private readStoredLanguage(): SupportedLanguage | null {
    try {
      const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
      return isSupportedLanguage(stored) ? stored : null;
    } catch {
      return null;
    }
  }

  private writeStoredLanguage(lang: SupportedLanguage): void {
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, lang);
    } catch {
      // Storage may be unavailable (private mode, quota): the choice just won't survive a reload.
    }
  }
}
