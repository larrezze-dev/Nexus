import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { nexusHttpRoutes } from './core/http/http.routes';
import { AuthenticatedShellComponent } from './core/layout/authenticated-shell.component';

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
    ],
  },
];
