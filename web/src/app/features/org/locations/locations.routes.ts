import { Routes } from '@angular/router';

/**
 * Location routes (ficha 0.4, track F), spliced into `app.routes.ts`'s authenticated shell
 * children.
 *
 * `locations/new` is declared before `locations/:id` so the Angular router matches the
 * literal segment first; both resolve to the same `LocationDetailPage`, which tells the create
 * form (no `id` param) apart from the detail page. The pages navigate with absolute paths
 * (`/locations/…`) because these routes are flat, not children of `locations`.
 */
export const locationsRoutes: Routes = [
  {
    path: 'locations',
    data: {
      navGroup: 'org',
      titleKey: 'nav.locations',
      icon: 'place',
      permission: 'ORG_STRUCTURE:READ',
    },
    loadComponent: () => import('./location-list.page').then((m) => m.LocationListPage),
  },
  {
    path: 'locations/new',
    data: {
      navGroup: 'org',
      listTitleKey: 'nav.locations',
      listPath: '/locations',
      crumbKey: 'breadcrumb.new.location',
      permission: 'ORG_STRUCTURE:CREATE',
    },
    loadComponent: () => import('./location-detail.page').then((m) => m.LocationDetailPage),
  },
  {
    path: 'locations/:id',
    data: {
      navGroup: 'org',
      listTitleKey: 'nav.locations',
      listPath: '/locations',
      detail: true,
      permission: 'ORG_STRUCTURE:READ',
    },
    loadComponent: () => import('./location-detail.page').then((m) => m.LocationDetailPage),
  },
];
