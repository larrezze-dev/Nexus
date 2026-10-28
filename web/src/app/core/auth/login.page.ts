import { Component, inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';

/**
 * NEXUS has no local login form: the IdP (Keycloak locally, Entra ID in Azure) IS the login
 * screen (ADR-0006, Authorization Code + PKCE — same contract as `auth.guard.ts`). This route
 * exists only so that `errorInterceptor`'s `router.navigateByUrl('/login')` on a 401 (and any
 * other direct navigation to `/login`) has somewhere to land: it immediately starts
 * `OidcSecurityService.authorize()`, which redirects the browser to the IdP, and renders
 * nothing of its own while that redirect is in flight.
 */
@Component({
  selector: 'nx-login-page',
  template: '',
})
export class LoginPage {
  private readonly oidcSecurityService = inject(OidcSecurityService);

  constructor() {
    this.oidcSecurityService.authorize();
  }
}
