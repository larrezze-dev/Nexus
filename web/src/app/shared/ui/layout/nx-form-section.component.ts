import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Form section card (`conventions-frontend.md §4.5`): white card (radius 12, border,
 * padding 24) with an `h2` title (18/24 500), an optional muted hint under it, and the
 * projected content (usually an `nx-form-grid`). Pass `id` to make it a target for
 * `nx-section-index`. Content marked `nxSectionActions` (e.g. a link to a related screen) sits
 * at the right of the title and wraps under it on narrow screens.
 */
@Component({
  selector: 'nx-form-section',
  standalone: true,
  template: `
    <header class="nx-form-section__header">
      <div class="nx-form-section__heading">
        <h2 class="nx-form-section__title">{{ title() }}</h2>
        @if (hint()) {
          <p class="nx-form-section__hint">{{ hint() }}</p>
        }
      </div>
      <div class="nx-form-section__actions">
        <ng-content select="[nxSectionActions]" />
      </div>
    </header>
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-4);
      padding: var(--nx-space-5);
      background: var(--nx-color-surface-raised);
      border: 1px solid var(--nx-color-border);
      border-radius: var(--nx-radius-lg);
      min-width: 0;
      scroll-margin-top: var(--nx-space-5);
    }

    .nx-form-section__header {
      display: flex;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: var(--nx-space-2) var(--nx-space-4);
    }

    .nx-form-section__heading {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-1);
      flex: 1 1 auto;
      min-width: 0;
    }

    .nx-form-section__actions {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--nx-space-2);
    }

    .nx-form-section__actions:empty {
      display: none;
    }

    .nx-form-section__title {
      margin: 0;
      font: var(--nx-text-section-title);
      color: var(--nx-color-text);
    }

    .nx-form-section__hint {
      margin: 0;
      font: var(--nx-text-label);
      color: var(--nx-color-text-muted);
    }

    @media (max-width: 599.98px) {
      :host {
        padding: var(--nx-space-4);
      }
    }
  `,
  host: { class: 'nx-form-section' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxFormSectionComponent {
  readonly title = input.required<string>();
  readonly hint = input<string | null | undefined>(undefined);
}
