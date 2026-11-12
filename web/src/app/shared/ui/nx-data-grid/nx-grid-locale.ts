import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Translation, TranslocoService } from '@jsverse/transloco';

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
 * Translations of the active UI language for `nx-data-grid`.
 *
 * - `translation`: the active language's (flattened) translation once its file has loaded,
 *   `null` before. Re-emits on every language switch.
 * - `localeText`: AG Grid `localeText` in the active language, or `null` while the file is
 *   not loaded yet. It never holds a raw i18n key: a key missing from the file is left out, so
 *   AG Grid falls back to its own text instead of showing `shared.grid.loadingOoo`.
 *
 * AG Grid reads `localeText` only while building the grid, so `nx-data-grid` mounts the grid
 * once `localeText` is not `null` and re-creates it when the language changes.
 */
@Injectable({ providedIn: 'root' })
export class NxGridLocale {
  private readonly transloco = inject(TranslocoService);

  readonly translation = toSignal<Translation | null>(this.transloco.selectTranslation(), {
    initialValue: null,
  });

  readonly localeText = computed<Readonly<Record<string, string>> | null>(
    () => {
      const translation = this.translation();
      if (!translation) {
        return null;
      }
      const text: Record<string, string> = {};
      for (const key of GRID_LOCALE_KEYS) {
        const value: unknown = translation[`shared.grid.${key}`];
        if (typeof value === 'string' && value !== '') {
          text[key] = value;
        }
      }
      return text;
    },
    // Same texts → same object: a translation re-emission must not re-create the grid.
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );

  /**
   * Text of `key` in the active language, or `''` while its file is not loaded (never the raw
   * key on first load). Reactive when called inside a `computed`.
   */
  text(key: string): string {
    const translation = this.translation();
    if (!translation) {
      return '';
    }
    const value: unknown = translation[key];
    return typeof value === 'string' ? value : this.transloco.translate(key);
  }
}
