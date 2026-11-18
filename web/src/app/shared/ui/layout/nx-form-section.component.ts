import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

let nextId = 0;

/**
 * Form section card (`conventions-frontend.md §4.5`): white card (radius 12, border,
 * padding 24) with an `h2` title (18/24 500), an optional muted hint under it, and the
 * projected content (usually an `nx-form-grid`). Pass `id` to make it a target for
 * `nx-section-index`. Content marked `nxSectionActions` (e.g. a link to a related screen) sits
 * at the right of the title and wraps under it on narrow screens.
 *
 * `collapsible` turns the section into a disclosure (WAI-ARIA disclosure pattern): the title is a
 * button inside the `h2` with `aria-expanded`/`aria-controls` and a chevron at the right, the hint
 * stays visible, and the body is `hidden` while closed (closed by default; `[(expanded)]` lets the
 * page open it, e.g. when a field inside has an error). For rarely used, technical fields.
 */
@Component({
  selector: 'nx-form-section',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <header class="nx-form-section__header">
      <div class="nx-form-section__heading">
        @if (collapsible()) {
          <h2 class="nx-form-section__title">
            <button
              type="button"
              class="nx-form-section__toggle"
              [attr.aria-expanded]="expanded()"
              [attr.aria-controls]="bodyId"
              (click)="toggle()"
            >
              <span class="nx-form-section__toggle-text">{{ title() }}</span>
              <mat-icon class="nx-form-section__chevron" aria-hidden="true">expand_more</mat-icon>
            </button>
          </h2>
        } @else {
          <h2 class="nx-form-section__title">{{ title() }}</h2>
        }
        @if (hint()) {
          <p class="nx-form-section__hint">{{ hint() }}</p>
        }
      </div>
      <div class="nx-form-section__actions">
        <ng-content select="[nxSectionActions]" />
      </div>
    </header>
    <div class="nx-form-section__body" [id]="bodyId" [hidden]="collapsible() && !expanded()">
      <ng-content />
    </div>
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

    .nx-form-section__body {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-4);
      min-width: 0;
    }

    .nx-form-section__body[hidden] {
      display: none;
    }

    /* Disclosure title: same type and left edge as a plain title, the whole row is the button. */
    .nx-form-section__toggle {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--nx-space-2);
      width: 100%;
      margin: 0;
      padding: 0;
      border: 0;
      border-radius: var(--nx-radius-sm);
      background: none;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .nx-form-section__toggle:focus-visible {
      outline: 2px solid var(--nx-color-primary);
      outline-offset: 2px;
    }

    .nx-form-section__chevron {
      flex: none;
      color: var(--nx-color-text-muted);
      transition: transform 150ms ease;
    }

    .nx-form-section__toggle[aria-expanded='true'] .nx-form-section__chevron {
      transform: rotate(180deg);
    }

    @media (prefers-reduced-motion: reduce) {
      .nx-form-section__chevron {
        transition: none;
      }
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
  /** Disclosure section: the title opens and closes the body. */
  readonly collapsible = input(false);
  /** Open state of a `collapsible` section (closed by default). */
  readonly expanded = model(false);

  protected readonly bodyId = `nx-form-section-body-${nextId++}`;

  protected toggle(): void {
    this.expanded.update((open) => !open);
  }
}
