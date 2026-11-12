import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * Footer action bar of detail and create forms (`conventions-frontend.md §4.5`): buttons
 * aligned to the right («Cancel» text button · primary flat button) above a top border.
 * Below 600 px it sticks to the bottom of the scrolling content, full-width and white, so the
 * primary action is always reachable.
 */
@Component({
  selector: 'nx-action-bar',
  standalone: true,
  template: '<ng-content />',
  styles: `
    :host {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: var(--nx-space-2);
      padding-top: var(--nx-space-4);
      border-top: 1px solid var(--nx-color-border);
    }

    @media (max-width: 599.98px) {
      :host {
        position: sticky;
        bottom: 0;
        z-index: 2;
        margin-inline: calc(-1 * var(--nx-content-padding));
        padding: var(--nx-space-3) var(--nx-content-padding);
        background: var(--nx-color-surface-raised);
      }
    }
  `,
  host: { class: 'nx-action-bar' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxActionBarComponent {}
