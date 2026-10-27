import { EnvironmentProviders, inject, isDevMode, provideAppInitializer } from '@angular/core';
import { provideTransloco } from '@jsverse/transloco';
import { NxI18nService } from './nx-i18n.service';
import { NxTranslocoHttpLoader } from './transloco-http-loader';
import { DEFAULT_LANGUAGE, SUPPORTED_LANGUAGES } from './supported-languages';

/**
 * Registers Transloco (`de`/`en`/`fr`/`it`/`es`, ADR-0010) and forces `NxI18nService` to be
 * instantiated eagerly, so the initial-language chain (browser language → `de`) runs before
 * the app renders. Add this once to `app.config.ts`.
 */
export function provideNexusI18n(): EnvironmentProviders[] {
  return [
    ...provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_LANGUAGES],
        defaultLang: DEFAULT_LANGUAGE,
        fallbackLang: DEFAULT_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: NxTranslocoHttpLoader,
    }),
    provideAppInitializer(() => {
      // Forces construction now, so the browser-language fallback is applied before first render.
      // `setInitialLanguage()` is called later by the integration step (F5) once `/me` is known.
      inject(NxI18nService);
    }),
  ];
}
