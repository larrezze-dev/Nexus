import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/** Shown when a request fails with 404 and for any unknown URL inside the shell. */
@Component({
  selector: 'nx-not-found-page',
  imports: [TranslocoPipe, RouterLink, MatButtonModule],
  template: `
    <section class="nx-empty-state" data-testid="nx-not-found-page">
      <h1>{{ 'httpPages.notFound.title' | transloco }}</h1>
      <p>{{ 'httpPages.notFound.message' | transloco }}</p>
      <a mat-flat-button routerLink="/account">{{ 'httpPages.goToAccount' | transloco }}</a>
    </section>
  `,
})
export class NotFoundPage {}
