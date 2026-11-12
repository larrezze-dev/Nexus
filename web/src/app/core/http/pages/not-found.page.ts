import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NxEmptyStateComponent } from '../../../shared/ui/layout/nx-empty-state.component';

/** Shown when a request fails with 404 and for any unknown URL inside the shell. */
@Component({
  selector: 'nx-not-found-page',
  imports: [TranslocoPipe, RouterLink, MatButtonModule, NxEmptyStateComponent],
  template: `
    <nx-empty-state
      kind="not-found"
      [title]="'httpPages.notFound.title' | transloco"
      [message]="'httpPages.notFound.message' | transloco"
      data-testid="nx-not-found-page"
    >
      <a mat-flat-button routerLink="/account">{{ 'httpPages.goToAccount' | transloco }}</a>
    </nx-empty-state>
  `,
})
export class NotFoundPage {}
