import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';

/**
 * Full-screen page (outside the shell, no `authGuard`) where the OIDC library lands when the
 * sign-in failed or the session expired (`unauthorizedRoute` in `auth.config.ts`).
 */
@Component({
  selector: 'nx-unauthorized-page',
  imports: [TranslocoPipe, MatButtonModule],
  template: `
    <section class="nx-empty-state" data-testid="nx-unauthorized-page">
      <h1>{{ 'httpPages.unauthorized.title' | transloco }}</h1>
      <p>{{ 'httpPages.unauthorized.message' | transloco }}</p>
      <button mat-flat-button type="button" (click)="signIn()" data-testid="nx-unauthorized-sign-in">
        {{ 'httpPages.unauthorized.signIn' | transloco }}
      </button>
    </section>
  `,
})
export class UnauthorizedPage {
  private readonly oidc = inject(OidcSecurityService);

  protected signIn(): void {
    this.oidc.authorize();
  }
}
