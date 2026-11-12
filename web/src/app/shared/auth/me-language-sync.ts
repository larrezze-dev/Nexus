import { HttpContext } from '@angular/common/http';
import { EnvironmentProviders, Injectable, inject, provideAppInitializer } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { filter, firstValueFrom } from 'rxjs';
import { Api } from '../../api/platform/api';
import { getMe } from '../../api/platform/fn/me/get-me';
import { NX_SKIP_ERROR_NAVIGATION } from '../../core/http/skip-error-navigation';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';

/**
 * Applies `GET /v1/me` -> `preferredLanguage` once, right after login, so the language does not
 * depend on the user visiting the account page (which is where `AccountFacade` also loads `/me`).
 * `NxI18nService.setInitialLanguage()` keeps an explicit `localStorage` choice above it.
 *
 * Best-effort: any failure leaves the browser/default language in place.
 */
@Injectable({ providedIn: 'root' })
export class MeLanguageSync {
  private readonly oidc = inject(OidcSecurityService);
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private started = false;

  /** Waits (in the background) for the first authenticated state, loads `/me` once and applies its language. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    void this.run();
  }

  private async run(): Promise<void> {
    try {
      await firstValueFrom(this.oidc.isAuthenticated$.pipe(filter(({ isAuthenticated }) => isAuthenticated)));
      const me = await this.api.invoke(getMe, undefined, new HttpContext().set(NX_SKIP_ERROR_NAVIGATION, true));
      this.i18n.setInitialLanguage(me.preferredLanguage);
    } catch {
      // Keep the browser/default language.
    }
  }
}

/** Starts `MeLanguageSync` at app start without blocking bootstrap. Add once to `app.config.ts`. */
export function provideMeLanguageSync(): EnvironmentProviders {
  return provideAppInitializer(() => {
    inject(MeLanguageSync).start();
  });
}
