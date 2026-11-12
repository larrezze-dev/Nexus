import { Routes } from '@angular/router';

/**
 * Routes for the "¿Qué ve este usuario?" effective-scope screen (`GET
 * /v1/users/{id}/effective-scope`). Exposed as a plain `Routes` array (not wired into
 * `app.routes.ts` here, out of scope for this subtask — the F6 integration subtask spreads it
 * into the authenticated shell's children, same pattern as `orgTreeRoutes`/`usersRoutes`).
 *
 * No `titleKey`/`icon` on this route: unlike `org/tree` or `users`, it is not meant to be its
 * own nav item — it is only reachable from the "Asignaciones" tab on a user's detail page
 * (`features/org/users/user-detail.page.html`).
 */
export const accessRoleAssignmentsRoutes: Routes = [
  {
    path: 'access/users/:id/effective-scope',
    data: {
      navGroup: 'people',
      listTitleKey: 'nav.users',
      listPath: '/users',
      crumbKey: 'breadcrumb.effectiveScope',
      permission: 'ROLE_ASSIGNMENT:READ',
    },
    loadComponent: () => import('./effective-scope.page').then((m) => m.EffectiveScopePage),
  },
];
