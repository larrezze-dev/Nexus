import type { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { switchMap } from 'rxjs';

/**
 * Attaches `Authorization: Bearer <access token>` only to requests whose
 * URL starts with `/api/` (the gateway, see `proxy.conf.json` /
 * `proxy.mock.json`). Every other request — third-party calls, the IdP
 * itself, static assets — goes through untouched.
 *
 * Assumes API calls use relative URLs (`/api/<service>/...`), which is the
 * convention for the `ng-openapi-gen`-generated clients (`conventions-api.md`).
 * An absolute URL to the same gateway would not match this check; that is
 * intentional, since NEXUS never calls its own gateway with a full origin
 * from the browser.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) {
    return next(req);
  }

  const oidcSecurityService = inject(OidcSecurityService);

  return oidcSecurityService.getAccessToken().pipe(
    switchMap((token) => {
      if (!token) {
        return next(req);
      }

      return next(
        req.clone({
          setHeaders: { Authorization: `Bearer ${token}` },
        }),
      );
    }),
  );
};
