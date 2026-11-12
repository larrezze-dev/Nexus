import { Routes } from '@angular/router';

/**
 * Lazy routes for the Users feature. Exposed as a standalone array (not
 * registered in `app.routes.ts` by this subtask) so the F6 integration
 * subtask can splice it into the authenticated shell's children.
 *
 * `titleKey`/`icon`/`permission`/`navGroup` live on the LIST route (the componentless parent
 * carries no data, so nothing is inherited by `new`/`:id`); `new`/`:id` carry the breadcrumb
 * data (`listTitleKey`, `listPath`, `crumbKey` | `detail`) — see `NxBreadcrumbService`.
 */
export const usersRoutes: Routes = [
  {
    path: 'users',
    children: [
      {
        path: '',
        pathMatch: 'full',
        data: {
          navGroup: 'people',
          titleKey: 'nav.users',
          icon: 'people',
          permission: 'USER:READ',
        },
        loadComponent: () => import('./user-list.page').then((m) => m.UserListPage),
      },
      {
        path: 'new',
        data: {
          navGroup: 'people',
          listTitleKey: 'nav.users',
          listPath: '/users',
          crumbKey: 'breadcrumb.new.user',
        },
        loadComponent: () => import('./user-detail.page').then((m) => m.UserDetailPage),
      },
      {
        path: ':id',
        data: {
          navGroup: 'people',
          listTitleKey: 'nav.users',
          listPath: '/users',
          detail: true,
        },
        loadComponent: () => import('./user-detail.page').then((m) => m.UserDetailPage),
      },
    ],
  },
];
