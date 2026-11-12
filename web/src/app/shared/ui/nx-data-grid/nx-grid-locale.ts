import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';

/** AG Grid `localeText` keys the Community grid surfaces in NEXUS, mapped to `shared.grid.*`. */
export const GRID_LOCALE_KEYS = [
  'loadingOoo',
  'noRowsToShow',
  'ariaSortableColumn',
  'ariaRowSelectAll',
  'ariaSkeletonCellLoading',
  'ariaSkeletonCellLoadingFailed',
  'loadingError',
] as const;

/**
 * `localeText` for AG Grid in the active UI language. The signal is re-computed when the
 * language (or its translation file) changes. AG Grid only reads `localeText` while building
 * the grid, so `nx-data-grid` re-creates the grid when the language changes.
 */
@Injectable({ providedIn: 'root' })
export class NxGridLocale {
  private readonly transloco = inject(TranslocoService);

  // Re-emits once the active language's file is loaded, and on every switch.
  private readonly translation = toSignal(this.transloco.selectTranslation());

  readonly localeText = computed<Record<string, string>>(() => {
    this.translation();
    const text: Record<string, string> = {};
    for (const key of GRID_LOCALE_KEYS) {
      text[key] = this.transloco.translate(`shared.grid.${key}`);
    }
    return text;
  });
}
