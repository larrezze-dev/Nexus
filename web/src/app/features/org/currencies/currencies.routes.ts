import { Routes } from '@angular/router';

/**
 * Routes for the Currencies screens: the list and the detail of one currency (by its ISO code).
 * A plain `Routes` array (same pattern as `../tree/tree.routes.ts`), spliced into the shell by
 * `app.routes.ts`. `org/currencies/new` is declared before `:code` so the literal segment wins.
 */
export const orgCurrenciesRoutes: Routes = [
  {
    path: 'org/currencies',
    data: {
      navGroup: 'org',
      titleKey: 'nav.orgCurrencies',
      icon: 'payments',
      permission: 'CURRENCY:READ',
    },
    loadComponent: () => import('./currency-list.page').then((m) => m.CurrencyListPage),
  },
  {
    path: 'org/currencies/new',
    data: {
      navGroup: 'org',
      listTitleKey: 'nav.orgCurrencies',
      listPath: '/org/currencies',
      crumbKey: 'breadcrumb.new.currency',
      permission: 'CURRENCY:CREATE',
    },
    loadComponent: () => import('./currency-create.page').then((m) => m.CurrencyCreatePage),
  },
  {
    path: 'org/currencies/:code',
    data: {
      navGroup: 'org',
      listTitleKey: 'nav.orgCurrencies',
      listPath: '/org/currencies',
      detail: true,
    },
    loadComponent: () => import('./currency-detail.page').then((m) => m.CurrencyDetailPage),
  },
];
