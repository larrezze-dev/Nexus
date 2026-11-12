import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Shown when a request fails with 403. `errorInterceptor` may append `?permission=...` to the
 * URL; it is deliberately not displayed (technical wording, not for end users).
 */
@Component({
  selector: 'nx-forbidden-page',
  imports: [TranslocoPipe, RouterLink, MatButtonModule],
  template: `
    <section class="nx-empty-state" data-testid="nx-forbidden-page">
      <h1>{{ 'httpPages.forbidden.title' | transloco }}</h1>
      <p>{{ 'httpPages.forbidden.message' | transloco }}</p>
      <a mat-flat-button routerLink="/account">{{ 'httpPages.goToAccount' | transloco }}</a>
    </section>
  `,
})
export class ForbiddenPage {}
