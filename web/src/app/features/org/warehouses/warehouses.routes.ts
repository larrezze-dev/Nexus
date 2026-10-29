import { Routes } from '@angular/router';

/**
 * Warehouse routes (ficha 0.4, track F). Exposed as a plain `Routes` array for the final
 * integration step (F6) to splice into `app.routes.ts`'s authenticated shell children —
 * this feature never touches `app.routes.ts` itself.
 *
 * `warehouses/new` is declared before `warehouses/:id` so the Angular router matches the
 * literal segment first; both resolve to the same `WarehouseDetailPage`, which reads
 * `ActivatedRoute`'s `id` param to tell "new" apart from "edit existing".
 */
export const warehousesRoutes: Routes = [
  {
    path: 'warehouses',
    data: { titleKey: 'nav.warehouses', icon: 'warehouse', permission: 'ORG_STRUCTURE:READ' },
    loadComponent: () => import('./warehouse-list.page').then((m) => m.WarehouseListPage),
  },
  {
    path: 'warehouses/new',
    data: { permission: 'ORG_STRUCTURE:CREATE' },
    loadComponent: () => import('./warehouse-detail.page').then((m) => m.WarehouseDetailPage),
  },
  {
    path: 'warehouses/:id',
    data: { permission: 'ORG_STRUCTURE:READ' },
    loadComponent: () => import('./warehouse-detail.page').then((m) => m.WarehouseDetailPage),
  },
];
