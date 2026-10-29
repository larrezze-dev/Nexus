import { Routes } from '@angular/router';

/**
 * Routes for the Currencies screen (ficha 0.4 track F point 2). A plain `Routes` array (same
 * pattern as `../tree/tree.routes.ts`), not wired into `app.routes.ts` here — F6 integrates it.
 */
export const orgCurrenciesRoutes: Routes = [
  {
    path: 'org/currencies',
    data: { titleKey: 'nav.orgCurrencies', icon: 'payments', permission: 'CURRENCY:READ' },
    loadComponent: () => import('./currency-list.page').then((m) => m.CurrencyListPage),
  },
];
