import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Page width: lists use the full content width; detail and create forms are capped at 1120 px. */
export type NxPageWidth = 'full' | 'form';

/**
 * Page container of every NEXUS screen (`conventions-frontend.md §4.3`). Stacks its children
 * (`nx-page-header`, cards, tabs, `nx-action-bar`) with a `--nx-space-5` gap. `width="form"`
 * caps the page at `--nx-page-max-width-form` and keeps it left-aligned, so breadcrumb, title
 * and form share the same left edge as on list pages.
 */
@Component({
  selector: 'nx-page',
  standalone: true,
  template: '<ng-content />',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      width: 100%;
      min-width: 0;
      box-sizing: border-box;
    }

    :host(.nx-page--form) {
      max-width: var(--nx-page-max-width-form);
    }
  `,
  host: {
    class: 'nx-page',
    '[class.nx-page--full]': "width() === 'full'",
    '[class.nx-page--form]': "width() === 'form'",
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxPageComponent {
  readonly width = input<NxPageWidth>('full');
}
