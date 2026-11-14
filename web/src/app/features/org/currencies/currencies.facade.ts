import { Injectable, inject, signal } from '@angular/core';
import { from } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { getCurrency } from '../../../api/platform/fn/currencies/get-currency';
import { listCurrencies } from '../../../api/platform/fn/currencies/list-currencies';
import { createCurrency } from '../../../api/platform/fn/currencies/create-currency';
import { updateCurrency } from '../../../api/platform/fn/currencies/update-currency';
import { CreateCurrencyRequest } from '../../../api/platform/models/create-currency-request';
import { Currency } from '../../../api/platform/models/currency';
import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxDataGridFetchFn, NxSortModel, Page } from '../../../shared/ui/page.model';

export interface CurrencyToggleError {
  readonly code: string;
  readonly message: string;
  /** `Problem.code` of the failure (e.g. `ORG-R7`), so the page can show its own translated text. */
  readonly problemCode?: string;
}

/**
 * Facade for the Currencies screens (ficha 0.4 track F point 2). Writes: create
 * (`POST /v1/currencies`), rename and activate/deactivate (both `PUT /v1/currencies/{code}`,
 * which needs `If-Match`). The code and the decimals are fixed after creation.
 *
 * `create()` lets the raw `HttpErrorResponse` propagate (a 409 `CONFLICT` means the code is taken:
 * the page marks the `code` control); `rename()` does too. `toggleActive()` records its failure
 * in `toggleError`, as the detail page shows ORG-R7 inline.
 */
@Injectable({ providedIn: 'root' })
export class CurrenciesFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly toggleErrorSignal = signal<CurrencyToggleError | null>(null);
  readonly toggleError = this.toggleErrorSignal.asReadonly();

  /** Last loaded currency and its `ETag` per code: what `PUT` must send as `If-Match`. */
  private readonly loaded = new Map<string, { currency: Currency; etag: string | null }>();

  /** `nx-data-grid` fetch function (`FE-R1`: the page never calls the generated API directly). */
  readonly fetchPage: NxDataGridFetchFn<CurrencyListItem> = (page, pageSize, sort) =>
    from(this.fetchCurrenciesPage(page, pageSize, sort));

  /** One currency (`GET /v1/currencies/{code}`), for the detail page; keeps its `ETag` for the next `PUT`. */
  async get(code: string): Promise<Currency> {
    const response = await this.api.invoke$Response(getCurrency, { code });
    this.loaded.set(code, { currency: response.body, etag: response.headers.get('ETag') });
    return response.body;
  }

  /** `ETag` of the currency as last loaded or saved, `null` if it was never loaded. */
  etag(code: string): string | null {
    return this.loaded.get(code)?.etag ?? null;
  }

  /** Creates a currency (`POST /v1/currencies`, ISO 4217 code, name in each language, decimals). */
  async create(request: CreateCurrencyRequest): Promise<Currency> {
    return this.api.invoke(createCurrency, { body: request });
  }

  /**
   * Renames a currency: `PUT` with the current `isActive` and the new `captionI18n`, `If-Match` =
   * `etag` (the one of the load; the stored one when omitted). The currency must have been loaded.
   */
  async rename(code: string, captionI18n: I18NText, etag?: string | null): Promise<Currency> {
    const current = this.loaded.get(code);
    if (!current) {
      throw new Error(`currency ${code} was not loaded`);
    }
    return this.put(code, { isActive: current.currency.isActive, captionI18n }, etag ?? current.etag);
  }

  clearToggleError(): void {
    this.toggleErrorSignal.set(null);
  }

  /**
   * Activates/deactivates a currency. `PUT` requires `If-Match` (`conventions-api.md §5`): this
   * sends the `ETag` of the load (`get()`), never a fresh read, so a change made by someone else
   * in between is a 409 and not silently overwritten. Only when the currency was never loaded does
   * it read once to get an `ETag`.
   *
   * Returns `true` on success. On failure — in particular ORG-R7 (a `Group`/`LegalEntity`
   * still references this currency while active, 422) — it records a readable `toggleError`
   * (the translated `Problem.code`) instead of a generic toast, and returns `false`; the page
   * displays `toggleError` inline.
   */
  async toggleActive(code: string, nextIsActive: boolean): Promise<boolean> {
    this.toggleErrorSignal.set(null);
    try {
      if (!this.loaded.has(code)) {
        await this.get(code);
      }
      const current = this.loaded.get(code);
      await this.put(code, { isActive: nextIsActive }, current?.etag ?? null);
      return true;
    } catch (error: unknown) {
      this.toggleErrorSignal.set(this.describeToggleError(code, error));
      return false;
    }
  }

  private async put(
    code: string,
    body: { isActive: boolean; captionI18n?: I18NText },
    etag: string | null,
  ): Promise<Currency> {
    const response = await this.api.invoke$Response(updateCurrency, {
      code,
      'If-Match': etag ?? undefined,
      body,
    });
    this.loaded.set(code, { currency: response.body, etag: response.headers.get('ETag') });
    return response.body;
  }

  private async fetchCurrenciesPage(
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Promise<Page<CurrencyListItem>> {
    const sortParam = sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined;
    const response = await this.api.invoke(listCurrencies, {
      page: page + 1,
      pageSize,
      sort: sortParam,
    });
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
      // Never `Problem.detail`: it is English server text.
      return { code, message: this.i18n.translateErrorCode(body.code), problemCode: body.code };
    }
    return { code, message: this.i18n.translateErrorCode(undefined) };
  }
}
