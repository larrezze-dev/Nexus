import { Routes } from '@angular/router';

/**
 * Routes for the read-only org structure tree (ficha 0.4 track F point 1).
 * Exposed as a plain `Routes` array (not wired into `app.routes.ts` here, out of scope for
 * this subtask — F6 spreads it into the authenticated shell's children, same pattern as the
 * existing `account` route in `app.routes.ts`).
 */
export const orgTreeRoutes: Routes = [
  {
    path: 'org/tree',
    data: { titleKey: 'nav.orgTree', icon: 'account_tree', permission: 'ORG_STRUCTURE:READ' },
    loadComponent: () => import('./org-tree.page').then((m) => m.OrgTreePage),
  },
];
