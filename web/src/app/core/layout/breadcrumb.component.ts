import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';

export interface NxBreadcrumb {
  readonly titleKey: string;
  readonly path: string;
}

/**
 * Breadcrumb trail built from the active route hierarchy's
 * `data.titleKey`, recomputed on every `NavigationEnd`.
 */
@Component({
  selector: 'app-breadcrumb',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './breadcrumb.component.html',
  styleUrl: './breadcrumb.component.scss',
})
export class BreadcrumbComponent {
  private readonly router = inject(Router);

  protected readonly crumbs = signal<NxBreadcrumb[]>(this.build());

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.crumbs.set(this.build()));
  }

  private build(): NxBreadcrumb[] {
    const crumbs: NxBreadcrumb[] = [];
    let snapshot: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    let path = '';

    while (snapshot) {
      const segment = snapshot.url.map((urlSegment) => urlSegment.path).join('/');
      if (segment) {
        path = `${path}/${segment}`;
      }

      const titleKey = snapshot.data['titleKey'] as string | undefined;
      if (titleKey) {
        crumbs.push({ titleKey, path: path || '/' });
      }

      snapshot = snapshot.firstChild;
    }

    return crumbs;
  }
}
