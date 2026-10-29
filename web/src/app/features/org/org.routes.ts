import { Routes } from '@angular/router';

/**
 * Routes for this subtask's two sub-features (`LegalEntity` + `BusinessUnit`
 * master data). A single file covering both, rather than one per
 * sub-feature, since they are a cohesive pair for this subtask and F6 only
 * needs one array to spread.
 *
 * Exported as a plain array, NOT wired into `app.routes.ts` here — the
 * integration subtask (F6) `...`-spreads `orgRoutes` into the authenticated
 * shell's `children`, same level as `account`. Each top-level entry carries
 * `data: { titleKey, icon, permission }` so `NavigationBuilderService`
 * (`core/layout/shell.component.ts`) picks it up for the side nav without
 * F6 having to repeat that metadata.
 *
 * Route shape per sub-feature: `''` → list, `'new'` → create form, `':id'`
 * → edit form (both backed by the same `*-detail.page`, which branches on
 * whether the `id` route param is present).
 */
export const orgRoutes: Routes = [
  {
    path: 'legal-entities',
    data: {
      titleKey: 'nav.org.legalEntities',
      icon: 'apartment',
      permission: 'ORG_STRUCTURE:READ',
    },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./legal-entities/legal-entity-list.page').then((m) => m.LegalEntityListPage),
      },
      {
        path: 'new',
        loadComponent: () =>
          import('./legal-entities/legal-entity-detail.page').then((m) => m.LegalEntityDetailPage),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./legal-entities/legal-entity-detail.page').then((m) => m.LegalEntityDetailPage),
      },
    ],
  },
  {
    path: 'business-units',
    data: {
      titleKey: 'nav.org.businessUnits',
      icon: 'store',
      permission: 'ORG_STRUCTURE:READ',
    },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./business-units/business-unit-list.page').then((m) => m.BusinessUnitListPage),
      },
      {
        path: 'new',
        loadComponent: () =>
          import('./business-units/business-unit-detail.page').then((m) => m.BusinessUnitDetailPage),
      },
      {
        path: ':id',
        loadComponent: () =>
          import('./business-units/business-unit-detail.page').then((m) => m.BusinessUnitDetailPage),
      },
    ],
  },
];
