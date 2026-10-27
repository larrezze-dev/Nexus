import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** Shown when a request fails with 404 (ficha 0.5, track F point 2). */
@Component({
  selector: 'nx-not-found-page',
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'httpPages.notFound.title' | transloco }}</h1>
    <p>{{ 'httpPages.notFound.message' | transloco }}</p>
  `,
})
export class NotFoundPage {}
