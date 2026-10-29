import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { CurrenciesFacade } from './currencies.facade';

/** Extra `colDef.cellRendererParams` this renderer expects, merged by AG Grid into `params`. */
export interface CurrencyActionCellExtraParams {
  readonly onChanged: () => void;
}

export type CurrencyActionCellParams = ICellRendererParams<CurrencyListItem> & CurrencyActionCellExtraParams;

/**
 * AG Grid cell renderer with the activate/deactivate button for one Currencies row
 * (`PUT /v1/currencies/{code}`). Calls `CurrenciesFacade.toggleActive` directly — the facade
 * is `providedIn: 'root'`, so no output binding is needed to reach it from inside a cell
 * renderer — then calls `onChanged()` (passed through `colDef.cellRendererParams`) so the
 * caller can trigger `nx-data-grid` to reload and the page can read the resulting
 * `toggleError`.
 */
@Component({
  selector: 'nx-currency-action-cell',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" [disabled]="busy" (click)="onToggle()">
      {{
        (isActive ? 'org.currencies.actions.deactivate' : 'org.currencies.actions.activate')
          | transloco
      }}
    </button>
  `,
})
export class CurrencyActionCellComponent implements ICellRendererAngularComp {
  private readonly facade = inject(CurrenciesFacade);

  protected code = '';
  protected isActive = false;
  protected busy = false;

  private params!: CurrencyActionCellParams;

  agInit(params: CurrencyActionCellParams): void {
    this.params = params;
    this.readRow(params);
  }

  refresh(params: CurrencyActionCellParams): boolean {
    this.params = params;
    this.readRow(params);
    return true;
  }

  protected async onToggle(): Promise<void> {
    this.busy = true;
    await this.facade.toggleActive(this.code, !this.isActive);
    this.busy = false;
    this.params.onChanged();
  }

  private readRow(params: CurrencyActionCellParams): void {
    this.code = params.data?.code ?? '';
    this.isActive = params.data?.isActive ?? false;
  }
}
