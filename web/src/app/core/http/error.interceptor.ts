import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ConcurrencyDialogService } from './concurrency-dialog.service';
import { isProblem } from './problem';

/**
 * Event dispatched on `window` when a request fails with 401, in addition to navigating to
 * `/login`. `core/auth` (another track, not touched here) can listen for it to drive its own
 * OIDC flow (e.g. trigger a silent refresh or redirect) without this interceptor depending on
 * it directly. See the design note on `errorInterceptor` below for the full rationale.
 */
export const NEXUS_UNAUTHORIZED_EVENT = 'nexus:auth:unauthorized';

/**
 * Maps every failed response's `Problem` body to the behavior described in ficha 0.5,
 * track F point 2 (conventions-api.md §5-6):
 *
 * - 400 (`validation`): not handled here. Re-thrown as-is so the component/form that issued
 *   the request reads `Problem.errors` (field → messages) and renders them itself.
 * - 401: navigates to `/login` and dispatches `NEXUS_UNAUTHORIZED_EVENT` on `window`. Two
 *   signals rather than one because this track cannot register a `/login` route or know how
 *   `core/auth`'s OIDC flow wants to be triggered; whichever one the integration step (F5)
 *   wires up is enough — the other is a harmless no-op.
 * - 403: navigates to `/forbidden?permission=<Problem.requiredPermission>` (query param, not
 *   router state, so the page is reachable and testable without relying on navigation history).
 * - 404: navigates to `/not-found`.
 * - 409 with `Problem.type === 'concurrency'`: does not navigate. Opens the
 *   `ConcurrencyDialogService` dialog instead, so the user stays on the page that triggered
 *   the conflict. Any other 409 is re-thrown as-is (e.g. the idempotent-create "different
 *   content" conflict from conventions-api.md §5, which a form must handle itself).
 * - 422 (`business_rule`): not handled here. Re-thrown as-is; the caller translates
 *   `Problem.code` via `NxI18nService.translateErrorCode()` (`errors.<code>`, generic fallback).
 *
 * In every case the original error is re-thrown after the side effect, so callers that need
 * to react themselves (e.g. a form clearing its "saving" state) still can.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const concurrencyDialog = inject(ConcurrencyDialogService);

  return next(req).pipe(
    catchError((error: unknown) => {
      // Only react to our own API (same scope as the auth interceptor's bearer token):
      // a 404 fetching an i18n asset or a 401 from a third-party request must not navigate
      // the whole app to /not-found or /login.
      if (error instanceof HttpErrorResponse && req.url.startsWith('/api/')) {
        const problem = isProblem(error.error) ? error.error : undefined;

        switch (error.status) {
          case 401: {
            window.dispatchEvent(new CustomEvent(NEXUS_UNAUTHORIZED_EVENT, { detail: { url: req.url } }));
            void router.navigateByUrl('/login');
            break;
          }
          case 403: {
            void router.navigate(['/forbidden'], {
              queryParams: { permission: problem?.requiredPermission ?? null },
            });
            break;
          }
          case 404: {
            void router.navigateByUrl('/not-found');
            break;
          }
          case 409: {
            if (problem?.type === 'concurrency') {
              concurrencyDialog.open();
            }
            break;
          }
          default:
            break;
        }
      }
      return throwError(() => error);
    }),
  );
};
