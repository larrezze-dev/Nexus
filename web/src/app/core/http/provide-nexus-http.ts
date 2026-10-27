import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { EnvironmentProviders } from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { acceptLanguageInterceptor } from './accept-language.interceptor';
import { correlationInterceptor } from './correlation.interceptor';
import { errorInterceptor } from './error.interceptor';

/**
 * Registers the `HttpClient`, the `X-Correlation-Id`/`Accept-Language`/error interceptors,
 * and the (async-loaded) Angular Material animations module the concurrency dialog needs.
 * Add this once to `app.config.ts`.
 *
 * Interceptor order matters: `correlationInterceptor` and `acceptLanguageInterceptor` set
 * request headers and can run in either order; `errorInterceptor` must be last so it sees
 * the final response/error after the others have run.
 */
export function provideNexusHttp(): EnvironmentProviders[] {
  return [
    provideHttpClient(
      withInterceptors([correlationInterceptor, acceptLanguageInterceptor, errorInterceptor]),
    ),
    provideAnimationsAsync(),
  ];
}
