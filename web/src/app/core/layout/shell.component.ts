import { NgTemplateOutlet } from '@angular/common';
import { Component, Input, TemplateRef, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';
import { NxCanDirective } from '../../shared/auth/can.directive';
import { BreadcrumbComponent } from './breadcrumb.component';
import { NavigationBuilderService } from './navigation-builder.service';
import { NxNavItem } from './nav-item.model';

/**
 * Application shell: top bar (logo, disabled global search, hidden
 * approvals bell, user menu) + side nav (built dynamically from the route
 * config by `NavigationBuilderService`) + breadcrumbs + `<router-outlet>`.
 *
 * Extension points left for other tracks / the final integration (F5):
 *
 * - `userMenuExtra` (`@Input() userMenuExtra?: TemplateRef<unknown>`):
 *   rendered inside the user `mat-menu` panel via `*ngTemplateOutlet`. F5
 *   wires the `language-selector` (from `core/i18n`) here, e.g.:
 *   ```html
 *   <app-shell [userMenuExtra]="extra">
 *     <ng-template #extra><app-language-selector /></ng-template>
 *   </app-shell>
 *   ```
 * - Nav items are wrapped with `*nxCan="item.permission"` (`shared/auth`),
 *   wired by F5 once `shared/auth` was merged into this worktree.
 * - The approvals bell is not rendered at all (ficha 0.5: hidden until
 *   0.9). 0.9 adds it back, it does not just toggle a flag.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterLink,
    RouterOutlet,
    NgTemplateOutlet,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatMenuModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    BreadcrumbComponent,
    NxCanDirective,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  private readonly navigationBuilder = inject(NavigationBuilderService);

  /** Projected into the user menu panel. See class doc for the F5 contract. */
  @Input() userMenuExtra?: TemplateRef<unknown>;

  protected readonly navItems = signal<NxNavItem[]>(this.navigationBuilder.build());
}
