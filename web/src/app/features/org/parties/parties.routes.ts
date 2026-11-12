import { Routes } from '@angular/router';

/**
 * Lazy routes for the Parties directory feature. Exposed as a standalone
 * array (not registered in `app.routes.ts` by this subtask) so the F6
 * integration subtask can splice it into the authenticated shell's
 * children — same pattern as `users.routes.ts`.
 */
export const partiesRoutes: Routes = [
  {
    path: 'parties',
    children: [
      {
        path: '',
        pathMatch: 'full',
        data: {
          navGroup: 'people',
          titleKey: 'nav.parties',
          icon: 'business',
          permission: 'PARTY:READ',
        },
        loadComponent: () => import('./party-list.page').then((m) => m.PartyListPage),
      },
      {
        path: 'new',
        data: {
          navGroup: 'people',
          listTitleKey: 'nav.parties',
          listPath: '/parties',
          crumbKey: 'breadcrumb.new.party',
        },
        loadComponent: () => import('./party-detail.page').then((m) => m.PartyDetailPage),
      },
      {
        path: ':id',
        data: {
          navGroup: 'people',
          listTitleKey: 'nav.parties',
          listPath: '/parties',
          detail: true,
        },
        loadComponent: () => import('./party-detail.page').then((m) => m.PartyDetailPage),
      },
    ],
  },
];
