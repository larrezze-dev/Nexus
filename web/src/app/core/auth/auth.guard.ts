import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map } from 'rxjs';

/**
 * Guard for authenticated routes.
 *
 * NEXUS has no local "/login" page: the IdP (Keycloak locally, Entra ID in
 * Azure) IS the login screen (ADR-0006, Authorization Code + PKCE). So an
 * unauthenticated navigation does not route anywhere internal — it starts
 * `OidcSecurityService.authorize()`, which redirects the browser to the IdP.
 */
export const authGuard: CanActivateFn = () => {
  const oidcSecurityService = inject(OidcSecurityService);

  return oidcSecurityService.isAuthenticated$.pipe(
    map(({ isAuthenticated }) => {
      if (isAuthenticated) {
        return true;
      }

      oidcSecurityService.authorize();
      return false;
    }),
  );
};
