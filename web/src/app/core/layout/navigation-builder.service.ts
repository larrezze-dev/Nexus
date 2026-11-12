import { Injectable, inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { NX_NAV_GROUPS, NxNavGroup, NxNavGroupId, NxNavItem } from './nav-item.model';

/**
 * Builds the side navigation items purely from the route configuration's
 * `data: { titleKey, icon, permission, navGroup }` (ficha 0.5, track F point 4).
 * Routes without `data.titleKey` are skipped (e.g. wildcard/redirect
 * routes, detail pages); their children, if any, are still walked and hoisted
 * to the parent level. A route with `data.nav === false` is kept out of the
 * side navigation even though it has a `titleKey` (e.g. "My account").
 *
 * Convention: `titleKey` lives on the LIST route only (never on a componentless
 * parent), so each section yields exactly one entry.
 *
 * Permission filtering (`*nxCan`) is done by the shell template, not here.
 */
@Injectable({ providedIn: 'root' })
export class NavigationBuilderService {
  private readonly router = inject(Router, { optional: true });

  build(routes?: Routes): NxNavItem[] {
    const source = routes ?? this.router?.config ?? [];
    return this.buildFrom(source, '');
  }

  /** Same items, bucketed by `navGroup` in `NX_NAV_GROUPS` order; ungrouped items are dropped. */
  buildGrouped(routes?: Routes): NxNavGroup[] {
    const items = this.build(routes);
    return NX_NAV_GROUPS.map((group) => ({
      navGroup: group.id as NxNavGroupId,
      titleKey: group.titleKey,
      items: items.filter((item) => item.navGroup === group.id),
    })).filter((group) => group.items.length > 0);
  }

  private buildFrom(routes: Routes, parentPath: string): NxNavItem[] {
    const items: NxNavItem[] = [];

    for (const route of routes) {
      const titleKey = route.data?.['titleKey'] as string | undefined;
      const path = this.joinPath(parentPath, route.path);
      const children = route.children ? this.buildFrom(route.children, path) : undefined;
      const hidden = route.data?.['nav'] === false;

      if (titleKey && !hidden) {
        const navGroup = route.data?.['navGroup'] as NxNavGroupId | undefined;
        items.push({
          titleKey,
          path,
          icon: route.data?.['icon'] as string | undefined,
          permission: route.data?.['permission'] as string | undefined,
          ...(navGroup ? { navGroup } : {}),
          ...(children && children.length > 0 ? { children } : {}),
        });
      } else if (children && children.length > 0) {
        // No titleKey of its own (e.g. a feature's lazy-loading shell route):
        // hoist its children to this level instead of losing them.
        items.push(...children);
      }
    }

    return items;
  }

  private joinPath(parentPath: string, segment: string | undefined): string {
    if (!segment) {
      return parentPath || '/';
    }
    const base = parentPath === '/' ? '' : parentPath;
    return `${base}/${segment}`;
  }
}
