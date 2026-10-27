import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

/**
 * Shown when a request fails with 403 (ficha 0.5, track F point 2). `errorInterceptor`
 * navigates here with `?permission=<RESOURCE:ACTION>` when the `Problem` carries
 * `requiredPermission`, so a direct link stays shareable/testable without router state.
 */
@Component({
  selector: 'nx-forbidden-page',
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'httpPages.forbidden.title' | transloco }}</h1>
    <p>{{ 'httpPages.forbidden.message' | transloco }}</p>
    @if (requiredPermission()) {
      <p>
        {{ 'httpPages.forbidden.requiredPermission' | transloco: { permission: requiredPermission() } }}
      </p>
    }
  `,
})
export class ForbiddenPage {
  private readonly route = inject(ActivatedRoute);

  readonly requiredPermission = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('permission'))),
    { initialValue: null },
  );
}
