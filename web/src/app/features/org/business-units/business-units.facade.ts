import { Injectable, inject, signal } from '@angular/core';
import { Observable, from } from 'rxjs';
import { map } from 'rxjs/operators';
import { Api } from '../../../api/platform/api';
import { createBusinessUnit } from '../../../api/platform/fn/business-units/create-business-unit';
import { deactivateBusinessUnit } from '../../../api/platform/fn/business-units/deactivate-business-unit';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { updateBusinessUnit } from '../../../api/platform/fn/business-units/update-business-unit';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { CreateBusinessUnitRequest } from '../../../api/platform/models/create-business-unit-request';
import { UpdateBusinessUnitRequest } from '../../../api/platform/models/update-business-unit-request';
import { NxSortModel, Page } from '../../../shared/ui/page.model';

/**
 * Facade for `BusinessUnit` master data — same shape as
 * `LegalEntitiesFacade` (`FE-R1`, `conventions-frontend.md §2/§3`): owns
 * `selected`/`loading`/`error` signals and the last-seen `ETag`, and
 * `create()`/`update()`/`deactivate()` let their `HttpErrorResponse`
 * propagate to the detail page rather than swallowing it.
 */
@Injectable({ providedIn: 'root' })
export class BusinessUnitsFacade {
  private readonly api = inject(Api);

  private readonly selectedSignal = signal<BusinessUnit | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private etag: string | null = null;

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** `nx-data-grid`'s `fetch` callback. `page` is 0-based; the API's `page` query param is 1-based. */
  list(page: number, pageSize: number, sort: NxSortModel | null): Observable<Page<BusinessUnitListItem>> {
    return from(
      this.api.invoke(listBusinessUnits, {
        page: page + 1,
        pageSize,
        sort: sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined,
      }),
    ).pipe(
      map((response) => ({
        items: response.items ?? [],
        page: (response.page ?? page + 1) - 1,
        pageSize: response.pageSize ?? pageSize,
        total: response.total ?? 0,
      })),
    );
  }

  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getBusinessUnit, { id });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
    } catch {
      this.selectedSignal.set(null);
      this.etag = null;
      this.errorSignal.set('load_failed');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async create(request: CreateBusinessUnitRequest): Promise<BusinessUnit> {
    const response = await this.api.invoke$Response(createBusinessUnit, { body: request });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  async update(id: string, request: UpdateBusinessUnitRequest): Promise<BusinessUnit> {
    const response = await this.api.invoke$Response(updateBusinessUnit, {
      id,
      body: request,
      'If-Match': this.etag ?? undefined,
    });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  async deactivate(id: string): Promise<BusinessUnit> {
    const response = await this.api.invoke$Response(deactivateBusinessUnit, {
      id,
      'If-Match': this.etag ?? undefined,
    });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  /** Clears `selected`/`error`/the stored `ETag` — call when entering the detail page fresh. */
  reset(): void {
    this.selectedSignal.set(null);
    this.errorSignal.set(null);
    this.etag = null;
  }
}
