import { Injectable, computed, inject, signal } from '@angular/core';
import { Data, NavigationEnd, NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';
import { navGroupTitleKey } from './nav-item.model';

/**
 * One crumb: either an i18n `key` or a literal `text` (a record name). `path` is set only on
 * crumbs that link somewhere; the section group and the current page are plain text.
 */
export interface NxCrumb {
  readonly key?: string;
  readonly text?: string;
  readonly path?: string;
}

/** What to show as the page title: an i18n key or a literal record name. */
export interface NxPageTitleSource {
  readonly key?: string;
  readonly text?: string;
}

/**
 * Route `data` contract (always set explicitly on the leaf route, nothing is inherited):
 * - `navGroup`: section group (`org` | `people` | `access`) -> first, non-link crumb.
 * - `titleKey`: LIST route (or single-page route) title; also the sidenav label.
 * - `listTitleKey` + `listPath`: on `new`/detail routes, the list crumb linking back.
 * - `crumbKey`: fixed last crumb for pages like "new" (no record behind).
 * - `detail: true`: last crumb is the record name from `setCurrentLabel`.
 * - `pageTitleKey`: title-only (no crumbs, no sidenav), e.g. error pages.
 */
export function leafRouteData(router: Router): Data {
  let snapshot = router.routerState.snapshot.root;
  while (snapshot.firstChild) {
    snapshot = snapshot.firstChild;
  }
  return snapshot.data;
}

export function buildTrail(data: Data, label: string | null): NxCrumb[] {
  const trail: NxCrumb[] = [];
  const groupKey = navGroupTitleKey(data['navGroup'] as string | undefined);
  if (groupKey) {
    trail.push({ key: groupKey });
  }
  const listTitleKey = data['listTitleKey'] as string | undefined;
  if (listTitleKey) {
    trail.push({ key: listTitleKey, path: data['listPath'] as string | undefined });
  }
  const titleKey = data['titleKey'] as string | undefined;
  const crumbKey = data['crumbKey'] as string | undefined;
  if (titleKey) {
    trail.push({ key: titleKey });
  } else if (crumbKey) {
    trail.push({ key: crumbKey });
  } else if (data['detail']) {
    trail.push(label ? { text: label } : { key: 'breadcrumb.loading' });
  }
  return trail;
}

export function pageTitleSource(data: Data, label: string | null): NxPageTitleSource | null {
  if (data['detail'] && !data['titleKey'] && !data['crumbKey']) {
    return label ? { text: label } : { key: data['listTitleKey'] as string | undefined };
  }
  const key = (data['pageTitleKey'] ?? data['titleKey'] ?? data['crumbKey']) as string | undefined;
  return key ? { key } : null;
}

/**
 * Holds the data of the active leaf route plus the "current record" label that detail pages
 * publish once their record has loaded. The label is cleared on every navigation start, so a
 * stale name never shows on the next page.
 */
@Injectable({ providedIn: 'root' })
export class NxBreadcrumbService {
  private readonly router = inject(Router);

  private readonly routeData = signal<Data>(leafRouteData(this.router));
  private readonly label = signal<string | null>(null);

  readonly currentLabel = this.label.asReadonly();
  readonly trail = computed(() => buildTrail(this.routeData(), this.label()));
  readonly pageTitle = computed(() => pageTitleSource(this.routeData(), this.label()));

  constructor() {
    this.router.events
      .pipe(filter((event) => event instanceof NavigationStart || event instanceof NavigationEnd))
      .subscribe((event) => {
        if (event instanceof NavigationStart) {
          this.label.set(null);
        } else {
          this.routeData.set(leafRouteData(this.router));
        }
      });
  }

  /** Detail pages call this once their record loaded (record name), `null` to clear it. */
  setCurrentLabel(text: string | null): void {
    this.label.set(text && text.trim() ? text : null);
  }
}
