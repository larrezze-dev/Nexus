import { Injectable, inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { NxNavItem } from './nav-item.model';

/**
 * Builds the side navigation items purely from the route configuration's
 * `data: { titleKey, icon, permission }` (ficha 0.5, track F point 4).
 * Routes without `data.titleKey` are skipped (e.g. wildcard/redirect
 * routes); their children, if any, are still walked and hoisted to the
 * parent level.
 *
 * Permission filtering (`*nxCan`) is intentionally NOT done here: this
 * worktree does not have `shared/auth` available. `ShellComponent` renders
 * every item regardless of `permission`; wiring `*nxCan="item.permission"`
 * around the nav link is left to the final integration (F5).
 */
@Injectable({ providedIn: 'root' })
export class NavigationBuilderService {
  private readonly router = inject(Router, { optional: true });

  build(routes?: Routes): NxNavItem[] {
    const source = routes ?? this.router?.config ?? [];
    return this.buildFrom(source, '');
  }

  private buildFrom(routes: Routes, parentPath: string): NxNavItem[] {
    const items: NxNavItem[] = [];

    for (const route of routes) {
      const titleKey = route.data?.['titleKey'] as string | undefined;
      const path = this.joinPath(parentPath, route.path);
      const children = route.children ? this.buildFrom(route.children, path) : undefined;

      if (titleKey) {
        items.push({
          titleKey,
          path,
          icon: route.data?.['icon'] as string | undefined,
          permission: route.data?.['permission'] as string | undefined,
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
