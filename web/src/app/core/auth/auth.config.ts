import type { EnvironmentProviders, Provider } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  provideAuth,
  withAppInitializerAuthCheck,
  type OpenIdConfiguration,
} from 'angular-auth-oidc-client';
import { environment } from '../../../environments/environment';
import { authInterceptor } from './auth.interceptor';

/**
 * `angular-auth-oidc-client` configuration (ADR-0006): Authorization Code +
 * PKCE, with refresh-token-based silent renewal. Entra ID in Azure,
 * Keycloak in local/dev — only `environment.auth` changes, the code below
 * is identical in both.
 */
export const nexusAuthConfig: OpenIdConfiguration = {
  authority: environment.auth.authority,
  clientId: environment.auth.clientId,
  scope: environment.auth.scope,
  responseType: 'code',
  redirectUrl: environment.auth.redirectUrl,
  postLogoutRedirectUri: environment.auth.postLogoutRedirectUri,
  useRefreshToken: true,
  silentRenew: true,
  autoUserInfo: true,
};

/**
 * Groups every provider `core/auth` needs so that app bootstrap
 * (`app.config.ts`, wired by the F5 integration subtask) only has to add
 * `provideNexusAuth()` to its `providers` array.
 *
 * IMPORTANT for whoever wires this into `app.config.ts` (F5): this calls
 * `provideHttpClient(withInterceptors([authInterceptor]))` itself, per the
 * 0.5 step card. If `core/http` (a parallel 0.5 subtask) ALSO calls
 * `provideHttpClient(...)` with its own interceptors (correlation id,
 * `Accept-Language`) at the same (root) injector level, verify with
 * `ng build`/`ng serve` that both sets of interceptors actually run —
 * Angular documents that multiple `HttpClient` instances in the SAME
 * injector are not a supported pattern (`withRequestsMadeViaParent` is only
 * about parent/child injectors, not siblings). If they conflict, prefer a
 * single `provideHttpClient(withInterceptors([authInterceptor, ...coreHttpInterceptors]))`
 * call in `app.config.ts` and drop the one embedded here — `authInterceptor`
 * is exported separately from `./auth.interceptor` for exactly that case.
 */
export function provideNexusAuth(): Array<Provider | EnvironmentProviders> {
  return [
    provideAuth({ config: nexusAuthConfig }, withAppInitializerAuthCheck()),
    provideHttpClient(withInterceptors([authInterceptor])),
  ];
}
