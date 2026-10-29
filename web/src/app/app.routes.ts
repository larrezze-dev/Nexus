import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { nexusHttpRoutes } from './core/http/http.routes';
import { AuthenticatedShellComponent } from './core/layout/authenticated-shell.component';
import { orgCurrenciesRoutes } from './features/org/currencies/currencies.routes';
import { locationsRoutes } from './features/org/locations/locations.routes';
import { orgRoutes } from './features/org/org.routes';
import { partiesRoutes } from './features/org/parties/parties.routes';
import { orgTreeRoutes } from './features/org/tree/tree.routes';
import { usersRoutes } from './features/org/users/users.routes';
import { warehousesRoutes } from './features/org/warehouses/warehouses.routes';

export const routes: Routes = [
  ...nexusHttpRoutes,
  {
    path: 'login',
    loadComponent: () => import('./core/auth/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    component: AuthenticatedShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'account' },
      {
        path: 'account',
        data: { titleKey: 'nav.account', icon: 'person' },
        loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
      },
      ...orgTreeRoutes,
      ...orgRoutes,
      ...warehousesRoutes,
      ...locationsRoutes,
      ...usersRoutes,
      ...partiesRoutes,
      ...orgCurrenciesRoutes,
    ],
  },
];
