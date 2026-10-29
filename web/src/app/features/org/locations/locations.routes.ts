import { Routes } from '@angular/router';

/**
 * Location routes (ficha 0.4, track F). Exposed as a plain `Routes` array for the final
 * integration step (F6) to splice into `app.routes.ts`'s authenticated shell children —
 * this feature never touches `app.routes.ts` itself.
 *
 * `locations/new` is declared before `locations/:id` so the Angular router matches the
 * literal segment first; both resolve to the same `LocationDetailPage`, which reads
 * `ActivatedRoute`'s `id` param to tell "new" apart from "edit existing".
 */
export const locationsRoutes: Routes = [
  {
    path: 'locations',
    data: { titleKey: 'nav.locations', icon: 'place', permission: 'ORG_STRUCTURE:READ' },
    loadComponent: () => import('./location-list.page').then((m) => m.LocationListPage),
  },
  {
    path: 'locations/new',
    data: { permission: 'ORG_STRUCTURE:CREATE' },
    loadComponent: () => import('./location-detail.page').then((m) => m.LocationDetailPage),
  },
  {
    path: 'locations/:id',
    data: { permission: 'ORG_STRUCTURE:READ' },
    loadComponent: () => import('./location-detail.page').then((m) => m.LocationDetailPage),
  },
];
