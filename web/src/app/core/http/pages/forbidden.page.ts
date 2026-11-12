import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NxEmptyStateComponent } from '../../../shared/ui/layout/nx-empty-state.component';

/**
 * Shown when a request fails with 403. `errorInterceptor` may append `?permission=...` to the
 * URL; it is deliberately not displayed (technical wording, not for end users).
 */
@Component({
  selector: 'nx-forbidden-page',
  imports: [TranslocoPipe, RouterLink, MatButtonModule, NxEmptyStateComponent],
  template: `
    <nx-empty-state
      kind="forbidden"
      [title]="'httpPages.forbidden.title' | transloco"
      [message]="'httpPages.forbidden.message' | transloco"
      data-testid="nx-forbidden-page"
    >
      <a mat-flat-button routerLink="/account">{{ 'httpPages.goToAccount' | transloco }}</a>
    </nx-empty-state>
  `,
})
export class ForbiddenPage {}
