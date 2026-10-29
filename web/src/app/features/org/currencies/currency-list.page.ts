import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { resolveCaption } from '../shared/caption.util';
import { CurrencyActionCellComponent, CurrencyActionCellExtraParams } from './currency-action-cell.component';
import { CurrenciesFacade } from './currencies.facade';
import { CurrencyStatusCellComponent } from './currency-status-cell.component';

/**
 * Currencies list (ficha 0.4 track F point 2): `GET /v1/currencies` (short, non-filtered
 * listing per the contract) through `nx-data-grid`, with inline activate/deactivate.
 *
 * ORG-R7 ("`legal_entity.currency_code`/`group.reporting_currency_code` apuntan a monedas
 * activas") can reject a deactivation with 422; `toggleError` below is shown as a dedicated,
 * readable banner (code + `Problem.detail`) instead of a generic toast, since it is
 * information the administrator acting on it needs to read, not just dismiss.
 */
@Component({
  selector: 'nx-currency-list-page',
  imports: [NxDataGridComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ 'org.currencies.title' | transloco }}</h1>

    @if (facade.toggleError(); as toggleError) {
      <div role="alert" class="nx-currencies__error" data-testid="nx-currencies-toggle-error">
        <strong>{{ toggleError.code }}:</strong> {{ toggleError.message }}
        <button type="button" (click)="facade.clearToggleError()">
          {{ 'common.cancel' | transloco }}
        </button>
      </div>
    }

    <nx-data-grid
      [columns]="columns()"
      [fetch]="facade.fetchPage"
      [filters]="{ refresh: refreshToken() }"
      [emptyMessageKey]="'org.currencies.empty'"
    />
  `,
  styles: `
    .nx-currencies__error {
      margin-bottom: 1rem;
      padding: 0.75rem 1rem;
      border: 1px solid currentColor;
      border-radius: 4px;
    }
  `,
})
export class CurrencyListPage {
  protected readonly facade = inject(CurrenciesFacade);

  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);

  /** Bumped after every successful/failed toggle to force `nx-data-grid` to reload
   * (its `fetch` reruns whenever `filters()` changes — see that component's docstring). */
  protected readonly refreshToken = signal(0);

  private readonly onRowChanged: CurrencyActionCellExtraParams['onChanged'] = () =>
    this.refreshToken.update((value) => value + 1);

  // Recomputed on language change so column headers follow the active UI language too
  // (`this.i18n.activeLang()` re-exports Transloco's own active-language signal).
  protected readonly columns = computed<ColDef<CurrencyListItem>[]>(() => {
    this.i18n.activeLang();
    const language = this.i18n.getActiveLanguage();

    return [
      { field: 'code', headerName: this.translate('org.currencies.columns.code'), sortable: true },
      {
        headerName: this.translate('org.currencies.columns.caption'),
        valueGetter: (params) =>
          resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
      },
      { field: 'minorUnits', headerName: this.translate('org.currencies.columns.minorUnits') },
      {
        headerName: this.translate('org.currencies.columns.status'),
        cellRenderer: CurrencyStatusCellComponent,
      },
      {
        headerName: this.translate('org.currencies.columns.action'),
        cellRenderer: CurrencyActionCellComponent,
        cellRendererParams: { onChanged: this.onRowChanged } satisfies CurrencyActionCellExtraParams,
      },
    ];
  });

  private translate(key: string): string {
    return this.transloco.translate(key);
  }
}
