import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideNexusAuth } from './core/auth/auth.config';
import { provideNexusHttp } from './core/http/provide-nexus-http';
import { provideNexusI18n } from './core/i18n/provide-nexus-i18n';
import { routes } from './app.routes';

/**
 * Provider order matters here: `provideHttpClient(withInterceptors(...))` is called once
 * inside `provideNexusAuth()` (`authInterceptor`) and once inside `provideNexusHttp()`
 * (`correlationInterceptor`, `acceptLanguageInterceptor`, `errorInterceptor`). Verified
 * (F5, step 0.5) with `HttpTestingController` that Angular accumulates interceptors across
 * multiple `provideHttpClient` calls in the same injector rather than one overriding the
 * other, and that they run in provider-registration order — so `provideNexusAuth()` must
 * stay before `provideNexusHttp()` for the final order to be
 * `authInterceptor -> correlationInterceptor -> acceptLanguageInterceptor -> errorInterceptor`
 * (error interceptor last, per `provide-nexus-http.ts`'s own doc comment).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideNexusAuth(),
    provideNexusI18n(),
    provideNexusHttp(),
  ],
};
