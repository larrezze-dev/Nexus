import { Injectable, inject, signal } from '@angular/core';
import { from } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { getCurrency } from '../../../api/platform/fn/currencies/get-currency';
import { listCurrencies } from '../../../api/platform/fn/currencies/list-currencies';
import { updateCurrency } from '../../../api/platform/fn/currencies/update-currency';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxDataGridFetchFn, NxSortModel, Page } from '../../../shared/ui/page.model';

export interface CurrencyToggleError {
  readonly code: string;
  readonly message: string;
}

/**
 * Facade for the Currencies list (ficha 0.4 track F point 2). The only write operation is
 * activate/deactivate (`PUT /v1/currencies/{code}`) — the contract has no create/edit by API
 * ("No hay alta por API": `CHF`/`EUR`/`USD`/`GBP` are seeded, `org.md §3`).
 */
@Injectable({ providedIn: 'root' })
export class CurrenciesFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly toggleErrorSignal = signal<CurrencyToggleError | null>(null);
  readonly toggleError = this.toggleErrorSignal.asReadonly();

  /** `nx-data-grid` fetch function (`FE-R1`: the page never calls the generated API directly). */
  readonly fetchPage: NxDataGridFetchFn<CurrencyListItem> = (page, pageSize, sort) =>
    from(this.fetchCurrenciesPage(page, pageSize, sort));

  clearToggleError(): void {
    this.toggleErrorSignal.set(null);
  }

  /**
   * Activates/deactivates a currency. `PUT` requires `If-Match` (`conventions-api.md §5`), so
   * this first re-reads the currency to get a fresh `ETag`, then sends the `PUT` with it.
   *
   * Returns `true` on success. On failure — in particular ORG-R7 (a `Group`/`LegalEntity`
   * still references this currency while active, 422) — it records a readable `toggleError`
   * (`Problem.detail`, falling back to the translated `Problem.code`) instead of a generic
   * toast, and returns `false`; the page displays `toggleError` inline.
   */
  async toggleActive(code: string, nextIsActive: boolean): Promise<boolean> {
    this.toggleErrorSignal.set(null);
    try {
      const current = await this.api.invoke$Response(getCurrency, { code });
      const etag = current.headers.get('ETag') ?? undefined;
      await this.api.invoke(updateCurrency, {
        code,
        'If-Match': etag,
        body: { isActive: nextIsActive },
      });
      return true;
    } catch (error: unknown) {
      this.toggleErrorSignal.set(this.describeToggleError(code, error));
      return false;
    }
  }

  private async fetchCurrenciesPage(
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Promise<Page<CurrencyListItem>> {
    const sortParam = sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined;
    const response = await this.api.invoke(listCurrencies, { page: page + 1, pageSize, sort: sortParam });
    return {
      items: response.items ?? [],
      page: response.page - 1,
      pageSize: response.pageSize,
      total: response.total,
    };
  }

  private describeToggleError(code: string, error: unknown): CurrencyToggleError {
    const body =
      error && typeof error === 'object' && 'error' in error
        ? (error as { error?: unknown }).error
        : undefined;
    if (isProblem(body)) {
      const message = body.detail ?? this.i18n.translateErrorCode(body.code);
      return { code, message };
    }
    return { code, message: this.i18n.translateErrorCode(undefined) };
  }
}
