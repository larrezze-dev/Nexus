import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { postLoginRedirect } from './core/auth/return-url';
import { nexusHttpRoutes } from './core/http/http.routes';
import { AuthenticatedShellComponent } from './core/layout/authenticated-shell.component';
import { accessRolesRoutes } from './features/access/roles/roles.routes';
import { accessRoleAssignmentsRoutes } from './features/access/role-assignments/role-assignments.routes';
import { orgCurrenciesRoutes } from './features/org/currencies/currencies.routes';
import { locationsRoutes } from './features/org/locations/locations.routes';
import { orgRoutes } from './features/org/org.routes';
import { partiesRoutes } from './features/org/parties/parties.routes';
import { orgTreeRoutes } from './features/org/tree/tree.routes';
import { usersRoutes } from './features/org/users/users.routes';
import { warehousesRoutes } from './features/org/warehouses/warehouses.routes';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./core/auth/login.page').then((m) => m.LoginPage),
  },
  {
    // Outside the shell and without authGuard: `unauthorizedRoute` of the OIDC config lands here.
    path: 'unauthorized',
    data: { pageTitleKey: 'httpPages.unauthorized.title' },
    loadComponent: () =>
      import('./core/auth/unauthorized.page').then((m) => m.UnauthorizedPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    component: AuthenticatedShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: postLoginRedirect },
      {
        path: 'account',
        // `nav: false`: reachable from the user menu, not listed in the sidenav.
        data: { titleKey: 'nav.account', icon: 'person', nav: false },
        loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
      },
      ...orgTreeRoutes,
      ...orgRoutes,
      ...locationsRoutes,
      ...warehousesRoutes,
      ...orgCurrenciesRoutes,
      ...usersRoutes,
      ...partiesRoutes,
      ...accessRolesRoutes,
      ...accessRoleAssignmentsRoutes,
      // Error pages + `**` wildcard: must stay LAST so they render inside the shell.
      ...nexusHttpRoutes,
    ],
  },
];
