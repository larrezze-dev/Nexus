import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, tap } from 'rxjs';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { CurrenciesFacade } from './currencies.facade';

/**
 * Currency list (`conventions-frontend.md §4.4`, list template): header with the record count
 * and the server-paginated `nx-data-grid` in a flush card.
 *
 * No «New currency» and no filter bar: the API has no create endpoint (the currencies are
 * seeded) and `GET /v1/currencies` only takes `page`/`pageSize`/`sort`. The API sorts by
 * `code`, `minorUnits` and `isActive`, so only those columns are sortable. The name is the
 * caption in the active language (columns are a `computed` of the language). A row (click,
 * Enter, card tap) opens the detail page, where the currency is activated or deactivated.
 */
@Component({
  selector: 'nx-currency-list-page',
  standalone: true,
  imports: [TranslocoModule, NxDataGridComponent, NxPageComponent, NxPageHeaderComponent],
  template: `
    <nx-page width="full">
      <nx-page-header
        [breadcrumb]="true"
        [title]="'org.currencies.title' | transloco"
        [count]="total()"
      />

      <section class="nx-card nx-card--flush">
        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [cardFields]="cardFields"
          emptyIcon="payments"
          [emptyMessageKey]="'org.currencies.empty'"
          (rowOpen)="open($event)"
        />
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrencyListPage {
  private readonly facade = inject(CurrenciesFacade);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  /** Computed so the currency name follows the active language. */
  protected readonly columns = computed<NxColDef<CurrencyListItem>[]>(() => {
    const language = this.i18n.activeLang();
    return [
      {
        field: 'code',
        headerKey: 'org.currencies.fields.code',
        cellClass: 'nx-mono',
        width: 140,
        sortable: true,
      },
      {
        colId: 'caption',
        headerKey: 'org.currencies.fields.caption',
        valueGetter: (params) =>
          resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
        flex: 2,
        sortable: false,
      },
      {
        field: 'minorUnits',
        headerKey: 'org.currencies.fields.minorUnits',
        width: 160,
        sortable: true,
      },
      {
        field: 'isActive',
        headerKey: 'org.currencies.fields.isActive',
        cellRenderer: OrgActiveStatusCellComponent,
        width: 140,
        sortable: true,
      },
    ];
  });

  /** Cards below 600 px: name, code underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'caption',
    subtitle: 'code',
    status: 'isActive',
  };

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Observable<Page<CurrencyListItem>> =>
    this.facade
      .fetchPage(page, pageSize, sort, {})
      .pipe(tap((result) => this.total.set(result.total)));

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<CurrencyListItem, 'code'>): void {
    void this.router.navigate(['/org/currencies', row.code]);
  }
}
