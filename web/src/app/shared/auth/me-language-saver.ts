import { Provider, inject } from '@angular/core';
import { Api } from '../../api/platform/api';
import { updateMe } from '../../api/platform/fn/me/update-me';
import { LanguagePreferenceSaver, NX_LANGUAGE_PREFERENCE_SAVER } from '../../core/i18n/nx-i18n.service';

/**
 * Saves the language the user picks in the top-bar selector as their preference
 * (`PATCH /v1/me` → `preferredLanguage`, `conventions-frontend.md §4.2`), so it follows them to
 * other browsers and devices. No `If-Match`: last write wins (contract decision 2026-11-12).
 *
 * `NxI18nService.setLanguage()` swallows failures: the language switch never waits for or
 * depends on this call.
 */
export function meLanguagePreferenceSaver(api: Api): LanguagePreferenceSaver {
  return async (language) => {
    await api.invoke(updateMe, { body: { preferredLanguage: language } });
  };
}

/** Replaces the default no-op `NX_LANGUAGE_PREFERENCE_SAVER`. Add once to `app.config.ts`. */
export function provideMeLanguageSaver(): Provider {
  return {
    provide: NX_LANGUAGE_PREFERENCE_SAVER,
    useFactory: () => meLanguagePreferenceSaver(inject(Api)),
  };
}
