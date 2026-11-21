import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Set to `true` in the `HttpContext` of a background request (e.g. `GET/PATCH /v1/me` fired by the
 * language sync) so `errorInterceptor` does not navigate to `/forbidden` or `/not-found` when it
 * fails: the user did not ask for that call, so a failure must not take them away from the page.
 * The error is still propagated to the caller; 401 still goes to `/login`.
 */
export const NX_SKIP_ERROR_NAVIGATION = new HttpContextToken<boolean>(() => false);

/**
 * Context for secondary lookups (names of referenced entities, picker searches, reference lists): a 403/404 must
 * leave «—» or an empty list, not navigate the user away from the page they asked for.
 */
export const namesContext = (): HttpContext => new HttpContext().set(NX_SKIP_ERROR_NAVIGATION, true);
