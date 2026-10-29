import { Routes } from '@angular/router';

/**
 * Lazy routes for the Users feature. Exposed as a standalone array (not
 * registered in `app.routes.ts` by this subtask) so the F6 integration
 * subtask can splice it into the authenticated shell's children.
 *
 * The parent route carries `data.titleKey`/`icon`/`permission` so
 * `NavigationBuilderService` (`core/layout`) turns it into exactly one nav
 * item; `list`/`new`/`:id` have no `titleKey` of their own and are not
 * hoisted as separate nav entries.
 */
export const usersRoutes: Routes = [
  {
    path: 'users',
    data: { titleKey: 'nav.users', icon: 'people', permission: 'USER:READ' },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./user-list.page').then((m) => m.UserListPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./user-detail.page').then((m) => m.UserDetailPage),
      },
      {
        path: ':id',
        loadComponent: () => import('./user-detail.page').then((m) => m.UserDetailPage),
      },
    ],
  },
];
