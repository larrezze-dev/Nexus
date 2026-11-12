import { Routes } from '@angular/router';

/**
 * Routes `errorInterceptor` navigates to on 403/404, plus the `**` wildcard. They MUST be
 * spread as the LAST children of the authenticated shell (see `app.routes.ts`), so the error
 * pages render inside the shell and an unknown URL ends in the not-found page.
 *
 * `errorInterceptor` also navigates to `/login` on 401 (owned by `core/auth`, outside the
 * shell), and the OIDC library redirects to `/unauthorized` (`core/auth/unauthorized.page.ts`).
 */
export const nexusHttpRoutes: Routes = [
  {
    path: 'forbidden',
    data: { pageTitleKey: 'httpPages.forbidden.title' },
    loadComponent: () => import('./pages/forbidden.page').then((m) => m.ForbiddenPage),
  },
  {
    path: 'not-found',
    data: { pageTitleKey: 'httpPages.notFound.title' },
    loadComponent: () => import('./pages/not-found.page').then((m) => m.NotFoundPage),
  },
  {
    path: '**',
    data: { pageTitleKey: 'httpPages.notFound.title' },
    loadComponent: () => import('./pages/not-found.page').then((m) => m.NotFoundPage),
  },
];
