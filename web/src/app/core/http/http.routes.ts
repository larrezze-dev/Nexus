import { Routes } from '@angular/router';

/**
 * Routes `errorInterceptor` navigates to on 403/404. Not registered here on purpose:
 * `app.routes.ts` is assembled by the integration step (F5) from every track's routes.
 * F5 should spread `nexusHttpRoutes` into the app's route table, e.g.:
 *
 * ```ts
 * export const routes: Routes = [...nexusHttpRoutes, ...otherRoutes];
 * ```
 *
 * `errorInterceptor` also navigates to `/login` on 401; that route is owned by `core/auth`
 * (another track) and is not included here.
 */
export const nexusHttpRoutes: Routes = [
  {
    path: 'forbidden',
    loadComponent: () => import('./pages/forbidden.page').then((m) => m.ForbiddenPage),
  },
  {
    path: 'not-found',
    loadComponent: () => import('./pages/not-found.page').then((m) => m.NotFoundPage),
  },
];
