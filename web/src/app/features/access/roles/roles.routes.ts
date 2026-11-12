import { Routes } from '@angular/router';

/**
 * Routes for the `Role` master data feature (`access.md §4`), same shape as
 * `org.routes.ts`'s `orgRoutes`: a top-level entry carrying
 * `data: { titleKey, icon, permission }` for `NavigationBuilderService`
 * (`core/layout/shell.component.ts`), with `''` → list, `'new'` → create
 * form, `':id'` → edit form (both backed by the same `role-detail.page`,
 * which branches on whether the `id` route param is present).
 *
 * Exported as a plain array, NOT wired into `app.routes.ts` here — a later
 * routing-integration subtask `...`-spreads `accessRolesRoutes` into the
 * authenticated shell's `children`.
 */
export const accessRolesRoutes: Routes = [
  {
    path: 'roles',
    children: [
      {
        path: '',
        pathMatch: 'full',
        data: {
          navGroup: 'access',
          titleKey: 'nav.access.roles',
          icon: 'admin_panel_settings',
          permission: 'ROLE:READ',
        },
        loadComponent: () => import('./role-list.page').then((m) => m.RoleListPage),
      },
      {
        path: 'new',
        data: {
          navGroup: 'access',
          listTitleKey: 'nav.access.roles',
          listPath: '/roles',
          crumbKey: 'breadcrumb.new.role',
        },
        loadComponent: () => import('./role-detail.page').then((m) => m.RoleDetailPage),
      },
      {
        path: ':id',
        data: {
          navGroup: 'access',
          listTitleKey: 'nav.access.roles',
          listPath: '/roles',
          detail: true,
        },
        loadComponent: () => import('./role-detail.page').then((m) => m.RoleDetailPage),
      },
    ],
  },
];
