import { ChangeDetectionStrategy, Component, DestroyRef, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { BreadcrumbComponent } from '../../../core/layout/breadcrumb.component';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { NxFormGridComponent, NxSpan, NxSpanDirective } from './nx-form-grid.component';

/** Field placeholders of the skeleton card: two L, then three M (one row each at 12 columns). */
const SKELETON_FIELDS: readonly NxSpan[] = ['l', 'l', 'm', 'm', 'm'];

/**
 * Loading state of a detail page (`conventions-frontend.md §4.7`: skeleton, never «Loading…»):
 * the breadcrumb (owned like `nx-page-header` does, so it does not jump when the record arrives),
 * grey bars where the title and the subtitle go, an optional tab bar and one form-section card
 * with field placeholders on the form grid. Screen readers hear one «Loading…» (`role="status"`);
 * the bars are hidden from them.
 *
 * ```html
 * <nx-page width="form">
 *   <nx-detail-skeleton [tabs]="true" />
 * </nx-page>
 * ```
 */
@Component({
  selector: 'nx-detail-skeleton',
  standalone: true,
  imports: [BreadcrumbComponent, TranslocoPipe, NxFormGridComponent, NxSpanDirective],
  template: `
    <div class="nx-detail-skeleton__header">
      <app-breadcrumb />
      <span class="cdk-visually-hidden" role="status">{{
        'shared.layout.detailSkeleton.loading' | transloco
      }}</span>
      <div class="nx-detail-skeleton__heading" aria-hidden="true">
        <span class="nx-detail-skeleton__title-line">
          <span class="nx-skeleton-bar nx-detail-skeleton__title"></span>
        </span>
        <span class="nx-detail-skeleton__subtitle-line">
          <span class="nx-skeleton-bar nx-detail-skeleton__subtitle"></span>
        </span>
      </div>
    </div>
    @if (tabs()) {
      <div class="nx-detail-skeleton__tabs" aria-hidden="true" data-testid="nx-detail-skeleton-tabs">
        <span class="nx-skeleton-bar nx-detail-skeleton__tab"></span>
        <span class="nx-skeleton-bar nx-detail-skeleton__tab"></span>
      </div>
    }
    <section class="nx-card nx-detail-skeleton__card" aria-hidden="true">
      <span class="nx-skeleton-bar nx-detail-skeleton__section-title"></span>
      <nx-form-grid>
        @for (span of fields; track $index) {
          <span class="nx-detail-skeleton__field" [nxSpan]="span">
            <span class="nx-skeleton-bar nx-detail-skeleton__field-bar"></span>
          </span>
        }
      </nx-form-grid>
    </section>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      min-width: 0;
    }

    .nx-detail-skeleton__header {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-4);
    }

    .nx-detail-skeleton__heading {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-1);
    }

    /* Bars centred in boxes as tall as the real title and subtitle lines: no jump on load. */
    .nx-detail-skeleton__title-line,
    .nx-detail-skeleton__subtitle-line {
      display: flex;
      align-items: center;
    }

    .nx-detail-skeleton__title-line {
      height: var(--nx-text-page-title-line);
    }

    .nx-detail-skeleton__subtitle-line {
      height: var(--nx-text-body-line);
    }

    .nx-detail-skeleton__title {
      width: min(40%, calc(10 * var(--nx-space-6)));
      height: var(--nx-space-5);
      border-radius: var(--nx-radius-sm);
    }

    .nx-detail-skeleton__subtitle {
      width: min(25%, calc(6 * var(--nx-space-6)));
    }

    .nx-detail-skeleton__tabs {
      display: flex;
      align-items: center;
      gap: var(--nx-space-5);
      height: calc(var(--nx-space-6) + var(--nx-space-4)); /* 48, Material tab bar */
      padding-inline: var(--nx-space-4);
      border-bottom: 1px solid var(--nx-color-border);
    }

    .nx-detail-skeleton__tab {
      width: calc(2 * var(--nx-space-6) + var(--nx-space-4));
    }

    .nx-detail-skeleton__card {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-4);
    }

    .nx-detail-skeleton__section-title {
      width: min(30%, calc(6 * var(--nx-space-6)));
      height: var(--nx-space-4);
    }

    .nx-detail-skeleton__field {
      display: flex;
      align-items: center;
      box-sizing: border-box;
      height: var(--nx-field-height);
      padding-inline: var(--nx-space-4);
      border: 1px solid var(--nx-color-border);
      border-radius: var(--nx-radius-sm);
    }

    .nx-detail-skeleton__field-bar {
      width: 50%;
    }
  `,
  host: { class: 'nx-detail-skeleton', 'data-testid': 'nx-detail-skeleton' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxDetailSkeletonComponent {
  /** Draws a tab bar placeholder (detail pages with General · … · Audit tabs). */
  readonly tabs = input(false);

  protected readonly fields = SKELETON_FIELDS;

  constructor() {
    // Same breadcrumb ownership as `nx-page-header`: the shell drops its own copy meanwhile.
    const release = inject(NxBreadcrumbService).claimForPage();
    inject(DestroyRef).onDestroy(release);
  }
}
