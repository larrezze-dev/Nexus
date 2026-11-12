import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { filter, map } from 'rxjs';
import { NxCanDirective } from '../../shared/auth/can.directive';
import { LanguageMenuComponent } from './language-menu.component';
import { NavigationBuilderService } from './navigation-builder.service';
import { NxNavGroup } from './nav-item.model';
import { UserMenuComponent } from './user-menu.component';

/** `localStorage` key remembering whether the side menu is shown at ≥ 1280 px (`'open'`/`'closed'`). */
export const SIDENAV_STORAGE_KEY = 'nx.sidenav';

/** From this width on the side menu sits beside the content (`side`) instead of over it (`over`). */
export const SIDENAV_EXPANDED_QUERY = '(min-width: 1280px)';

/**
 * Application shell (`conventions-frontend.md §4.2`): white 56 px top bar (☰, NEXUS, language
 * menu, user menu), a grouped 248 px side menu built from the route config by
 * `NavigationBuilderService.buildGrouped()`, and a full-height `<main>` hosting the page.
 *
 * Side menu behaviour:
 * - ≥ 1280 px: `side`, open by default; ☰ hides/shows it entirely and the choice is remembered
 *   in `localStorage['nx.sidenav']`.
 * - < 1280 px: `over`, closed on entry; ☰ opens it above the content; it closes on navigation,
 *   on the backdrop and on Esc.
 *
 * Breadcrumbs are not rendered here: each page renders them through `nx-page-header`.
 * The approvals bell is not rendered at all (ficha 0.5: hidden until 0.9).
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    TranslocoPipe,
    NxCanDirective,
    LanguageMenuComponent,
    UserMenuComponent,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly navigationBuilder = inject(NavigationBuilderService);
  private readonly router = inject(Router);

  protected readonly navGroups = signal<NxNavGroup[]>(this.navigationBuilder.buildGrouped());

  /** `true` at ≥ 1280 px. */
  protected readonly expanded = toSignal(
    inject(BreakpointObserver)
      .observe(SIDENAV_EXPANDED_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  private readonly expandedOpen = signal(readStoredSidenavOpen());
  private readonly compactOpen = signal(false);

  protected readonly mode = computed(() => (this.expanded() ? 'side' : 'over'));
  protected readonly opened = computed(() => (this.expanded() ? this.expandedOpen() : this.compactOpen()));

  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.compactOpen.set(false));
  }

  protected toggle(): void {
    this.setOpened(!this.opened());
  }

  /** Mirrors opens/closes made by the sidenav itself (backdrop click, Esc) into our state. */
  protected onOpenedChange(open: boolean): void {
    if (open !== this.opened()) {
      this.setOpened(open);
    }
  }

  private setOpened(open: boolean): void {
    if (this.expanded()) {
      this.expandedOpen.set(open);
      writeStoredSidenavOpen(open);
    } else {
      this.compactOpen.set(open);
    }
  }
}

function readStoredSidenavOpen(): boolean {
  try {
    return localStorage.getItem(SIDENAV_STORAGE_KEY) !== 'closed';
  } catch {
    return true;
  }
}

function writeStoredSidenavOpen(open: boolean): void {
  try {
    localStorage.setItem(SIDENAV_STORAGE_KEY, open ? 'open' : 'closed');
  } catch {
    // Storage unavailable (private mode, quota): the choice just won't survive a reload.
  }
}
