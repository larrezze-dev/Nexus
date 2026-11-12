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
 * Resolution (conventions-frontend.md §4.2): the user's preference from `GET /v1/me` rules once it
 * arrives. `localStorage['nx.lang']` only exists to paint the first frame in the right language
 * before `/me` is known; `setInitialLanguage()` applies `preferredLanguage` (without calling the
 * saver) and rewrites `nx.lang` with it. If the user already picked a language in the selector
 * in this session, that choice stands (it was already saved through the saver). Without a
 * usable preference the order is: stored value, browser language (`navigator.language`), `de`.
 * `<html lang>` always mirrors the active language. Logging out clears `nx.lang`.
 */
@Injectable({ providedIn: 'root' })
export class NxI18nService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  private readonly saver = inject(NX_LANGUAGE_PREFERENCE_SAVER);

  /** Signal that reflects the currently active language (re-exported from Transloco for convenience). */
  readonly activeLang: Signal<string> = this.transloco.activeLang;

  /** `true` once the user picked a language in the selector during this session. */
  private chosenThisSession = false;

  constructor() {
    this.transloco.langChanges$.subscribe((lang) => {
      this.document.documentElement.lang = lang;
    });
    this.transloco.setActiveLang(this.resolve(null));
  }

  /**
   * Applies `preferredLanguage` (from `/me`) as the active language and remembers it in
   * `nx.lang`. Never calls the saver: the value already comes from the server. Ignored when the
   * user has picked a language in the selector in this session. An unusable preference leaves
   * the language resolved at construction.
   */
  setInitialLanguage(preferredLanguage: string | null | undefined): void {
    if (this.chosenThisSession) {
      return;
    }
    const preferred = normalizeToSupportedLanguage(preferredLanguage);
    if (preferred === null) {
      this.transloco.setActiveLang(this.resolve(null));
      return;
    }
    this.transloco.setActiveLang(preferred);
    this.writeStoredLanguage(preferred);
  }

  /** Sets the active language from an explicit user choice: remembered locally and handed to the saver hook. */
  setLanguage(lang: SupportedLanguage): void {
    this.chosenThisSession = true;
    this.transloco.setActiveLang(lang);
    this.writeStoredLanguage(lang);
    try {
      void Promise.resolve(this.saver(lang)).catch(() => undefined);
    } catch {
      // The preference is a convenience: a failing saver never blocks the language switch.
    }
  }

  /** Forgets the remembered language (on sign-out) so the next user on this browser does not inherit it. */
  clearStoredLanguage(): void {
    try {
      localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    } catch {
      // Storage unavailable: nothing to clear.
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
