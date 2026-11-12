import { ChangeDetectionStrategy, Component, Directive, ViewEncapsulation, input } from '@angular/core';

/** Field width in the 12-column form grid: S = 3, M = 4, L = 6, XL = 12 columns. */
export type NxSpan = 's' | 'm' | 'l' | 'xl';

/**
 * Form grid (`conventions-frontend.md §4.3`): CSS grid of 12 columns (≥ 1280 px), 6 columns
 * (600–1279 px, where S/M take 3 and L/XL take 6) and 1 column (< 600 px). Column gap
 * `--nx-form-gap-x` (24), row gap `--nx-form-gap-y` (16). Each child declares its width with
 * `nxSpan="s|m|l|xl"`; a child without `nxSpan` takes the whole row. Never fixed px widths.
 *
 * Styles are not encapsulated (they target projected children) and are scoped by the
 * `.nx-form-grid` / `.nx-span--*` class names.
 */
@Component({
  selector: 'nx-form-grid',
  standalone: true,
  template: '<ng-content />',
  styles: `
    .nx-form-grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      column-gap: var(--nx-form-gap-x);
      row-gap: var(--nx-form-gap-y);
      align-items: start;
      min-width: 0;
    }

    .nx-form-grid > * {
      grid-column: 1 / -1;
      min-width: 0;
    }

    .nx-form-grid > .mat-mdc-form-field {
      width: 100%;
    }

    .nx-form-grid > .nx-span--s {
      grid-column: span 3;
    }

    .nx-form-grid > .nx-span--m {
      grid-column: span 4;
    }

    .nx-form-grid > .nx-span--l {
      grid-column: span 6;
    }

    .nx-form-grid > .nx-span--xl {
      grid-column: span 12;
    }

    @media (max-width: 1279.98px) {
      .nx-form-grid {
        grid-template-columns: repeat(6, minmax(0, 1fr));
      }

      .nx-form-grid > .nx-span--s,
      .nx-form-grid > .nx-span--m {
        grid-column: span 3;
      }

      .nx-form-grid > .nx-span--l,
      .nx-form-grid > .nx-span--xl {
        grid-column: span 6;
      }
    }

    @media (max-width: 599.98px) {
      .nx-form-grid {
        grid-template-columns: minmax(0, 1fr);
      }

      .nx-form-grid > .nx-span--s,
      .nx-form-grid > .nx-span--m,
      .nx-form-grid > .nx-span--l,
      .nx-form-grid > .nx-span--xl {
        grid-column: 1 / -1;
      }
    }
  `,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'nx-form-grid' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxFormGridComponent {}

/** Width of one `nx-form-grid` child: `<mat-form-field nxSpan="s">`. */
@Directive({
  selector: '[nxSpan]',
  standalone: true,
  host: {
    '[class.nx-span--s]': "nxSpan() === 's'",
    '[class.nx-span--m]': "nxSpan() === 'm'",
    '[class.nx-span--l]': "nxSpan() === 'l'",
    '[class.nx-span--xl]': "nxSpan() === 'xl'",
  },
})
export class NxSpanDirective {
  readonly nxSpan = input.required<NxSpan>();
}
