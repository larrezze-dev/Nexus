import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  inject,
  input,
  output,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';

/**
 * Filter row at the top of a list card (`conventions-frontend.md §4.4`): the projected filters
 * (`mat-form-field appearance="outline"`, label inside the field) in one row that wraps, and a
 * «Clear filters» text button at the end that emits `clear`. The search field takes the
 * `nx-filter--search` class to be wider than the selects.
 *
 * Styles are not encapsulated (they size the projected form fields) and are scoped by the
 * `.nx-filter-bar` class name.
 */
@Component({
  selector: 'nx-filter-bar',
  standalone: true,
  imports: [MatButtonModule, TranslocoPipe],
  template: `
    <ng-content />
    @if (showClear()) {
      <button
        mat-button
        type="button"
        class="nx-filter-bar__clear"
        data-testid="nx-filter-bar-clear"
        (click)="clear.emit()"
      >
        {{ 'shared.layout.filterBar.clear' | transloco }}
      </button>
    }
  `,
  styles: `
    .nx-filter-bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--nx-space-3);
      padding: var(--nx-space-4);
      border-bottom: 1px solid var(--nx-color-border);
      background: var(--nx-color-surface-raised);
    }

    /* Filters sit in one row: no reserved hint/error space under each field. */
    .nx-filter-bar > .mat-mdc-form-field {
      flex: 0 1 200px;
      min-width: 160px;
    }

    .nx-filter-bar > .mat-mdc-form-field .mat-mdc-form-field-subscript-wrapper {
      display: none;
    }

    .nx-filter-bar > .mat-mdc-form-field.nx-filter--search {
      flex: 0 1 320px;
      min-width: 200px;
    }

    @media (max-width: 599.98px) {
      .nx-filter-bar {
        padding: 0 0 var(--nx-space-3);
        border-bottom: 0;
        background: transparent;
      }

      .nx-filter-bar > .mat-mdc-form-field,
      .nx-filter-bar > .mat-mdc-form-field.nx-filter--search {
        flex: 1 1 100%;
        min-width: 0;
      }
    }
  `,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'nx-filter-bar', role: 'search', '[attr.aria-label]': 'ariaLabel() || null' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxFilterBarComponent {
  /** Hide «Clear filters» when the list has nothing to clear (e.g. no filter set). */
  readonly showClear = input<boolean>(true);

  /** «Clear filters» was pressed: the page resets its filter controls. */
  readonly clear = output<void>();

  protected readonly ariaLabel = toSignal(
    inject(TranslocoService).selectTranslate<string>('shared.layout.filterBar.ariaLabel'),
    { initialValue: '' },
  );
}
