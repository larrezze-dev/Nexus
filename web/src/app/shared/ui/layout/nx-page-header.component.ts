import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { BreadcrumbComponent } from '../../../core/layout/breadcrumb.component';

/**
 * Page header (`conventions-frontend.md §4.4–§4.6`): breadcrumb, then one row with the page
 * title (`h1`, 28/36 500), an optional status chip (`[nxHeaderStatus]` slot) and record count,
 * the subtitle underneath (muted 14 — e.g. code · last modified) and the actions on the right
 * (`[nxHeaderActions]` slot). Wraps below the title on narrow screens.
 *
 * ```html
 * <nx-page-header [title]="name()" [subtitle]="code()">
 *   <nx-status-badge nxHeaderStatus [status]="status()" />
 *   <button nxHeaderActions mat-stroked-button class="nx-btn--danger">…</button>
 * </nx-page-header>
 * ```
 */
@Component({
  selector: 'nx-page-header',
  standalone: true,
  imports: [BreadcrumbComponent, TranslocoPipe],
  template: `
    <!-- TODO(PR-5): the shell renders the breadcrumb for now; pages pass [breadcrumb]="true" and the shell drops it. -->
    @if (breadcrumb()) {
      <app-breadcrumb />
    }
    <div class="nx-page-header__row">
      <div class="nx-page-header__heading">
        <div class="nx-page-header__title-line">
          <h1 class="nx-page-header__title">{{ title() }}</h1>
          <ng-content select="[nxHeaderStatus]" />
          @if (count() !== null && count() !== undefined) {
            <span class="nx-page-header__count" data-testid="nx-page-header-count">{{
              (count() === 1
                ? 'shared.layout.pageHeader.countOne'
                : 'shared.layout.pageHeader.count'
              ) | transloco: { count: count() }
            }}</span>
          }
        </div>
        @if (subtitle()) {
          <p class="nx-page-header__subtitle">{{ subtitle() }}</p>
        }
      </div>
      <div class="nx-page-header__actions">
        <ng-content select="[nxHeaderActions]" />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-4);
      min-width: 0;
    }

    .nx-page-header__row {
      display: flex;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: var(--nx-space-4);
    }

    .nx-page-header__heading {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-1);
      flex: 1 1 auto;
      min-width: 0;
    }

    .nx-page-header__title-line {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      column-gap: var(--nx-space-3);
      row-gap: var(--nx-space-1);
      min-height: var(--nx-text-page-title-line);
    }

    .nx-page-header__title {
      margin: 0;
      font: var(--nx-text-page-title);
      color: var(--nx-color-text);
      overflow-wrap: anywhere;
    }

    .nx-page-header__count,
    .nx-page-header__subtitle {
      margin: 0;
      font: var(--nx-text-body);
      color: var(--nx-color-text-muted);
    }

    .nx-page-header__actions {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--nx-space-2);
      min-height: var(--nx-text-page-title-line);
    }

    .nx-page-header__actions:empty {
      display: none;
    }
  `,
  host: { class: 'nx-page-header' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxPageHeaderComponent {
  /**
   * Renders the breadcrumb inside the header. `false` for now because the shell renders it
   * (TODO(PR-5): flip the default to `true` when the pages migrate and remove it from the shell).
   */
  readonly breadcrumb = input(false);

  /** Page title (already translated): list name, record name or «New X». */
  readonly title = input.required<string>();

  /** Muted line under the title (already translated), e.g. `WH-BCN-01 · modified 11.11.2026`. */
  readonly subtitle = input<string | null | undefined>(undefined);

  /** Total number of records on list pages; shown as «4 records» next to the title. */
  readonly count = input<number | null | undefined>(undefined);
}
