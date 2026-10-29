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
    data: { titleKey: 'nav.parties', icon: 'business', permission: 'PARTY:READ' },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./party-list.page').then((m) => m.PartyListPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./party-detail.page').then((m) => m.PartyDetailPage),
      },
      {
        path: ':id',
        loadComponent: () => import('./party-detail.page').then((m) => m.PartyDetailPage),
      },
    ],
  },
];
