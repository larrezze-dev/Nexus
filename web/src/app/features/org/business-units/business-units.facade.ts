import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, defer, forkJoin, from, of } from 'rxjs';
import { map } from 'rxjs/operators';
import { Api } from '../../../api/platform/api';
import { createBusinessUnit } from '../../../api/platform/fn/business-units/create-business-unit';
import { deactivateBusinessUnit } from '../../../api/platform/fn/business-units/deactivate-business-unit';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { getParty } from '../../../api/platform/fn/parties/get-party';
import { updateBusinessUnit } from '../../../api/platform/fn/business-units/update-business-unit';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { CreateBusinessUnitRequest } from '../../../api/platform/models/create-business-unit-request';
import { Party } from '../../../api/platform/models/party';
import { UpdateBusinessUnitRequest } from '../../../api/platform/models/update-business-unit-request';
import { NxSortModel, Page } from '../../../shared/ui/page.model';

/** A legal entity as the business-unit screens show it: code and legal name, never the id. */
export interface BusinessUnitLegalEntityOption {
  readonly id: string;
  readonly code: string;
  readonly legalName: string;
  readonly isActive: boolean;
}

/** A list row: the API item plus its legal entity (`null` if it could not be read). */
export interface BusinessUnitRow extends BusinessUnitListItem {
  readonly legalEntity: BusinessUnitLegalEntityOption | null;
}

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
  private readonly partySignal = signal<Party | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private etag: string | null = null;

  readonly selected = this.selectedSignal.asReadonly();
  /** `Party` of the selected unit (legal name, short name), loaded by `load()`. */
  readonly party = this.partySignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /**
   * `nx-data-grid`'s `fetch` callback. `page` is 0-based; the API's `page` query param is 1-based.
   * `GET /v1/business-units` has no filters (only `page`/`pageSize`/`sort` by `code`, `isActive`,
   * `createdAt`, `updatedAt`). Each row carries its legal entity (code + legal name), read from
   * the legal-entity list in the same round trip, so the grid never shows an id.
   */
  list(page: number, pageSize: number, sort: NxSortModel | null): Observable<Page<BusinessUnitRow>> {
    const units = from(
      this.api.invoke(listBusinessUnits, {
        page: page + 1,
        pageSize,
        sort: sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined,
      }),
    );
    const legalEntities = defer(() => this.legalEntityOptions()).pipe(
      catchError(() => of<BusinessUnitLegalEntityOption[]>([])),
    );
    return forkJoin([units, legalEntities]).pipe(
      map(([response, options]) => {
        const byId = new Map(options.map((option) => [option.id, option]));
        return {
          items: (response.items ?? []).map((item) => ({ ...item, legalEntity: byId.get(item.legalEntityId) ?? null })),
          page: (response.page ?? page + 1) - 1,
          pageSize: response.pageSize ?? pageSize,
          total: response.total ?? 0,
        };
      }),
    );
  }

  /** Legal entities (active and inactive) by code, for the «Legal entity» column and select. */
  async legalEntityOptions(): Promise<BusinessUnitLegalEntityOption[]> {
    const response = await this.api.invoke(listLegalEntities, { page: 1, pageSize: 200, sort: 'code' });
    return (response.items ?? []).map((item) => ({
      id: item.id,
      code: item.code,
      legalName: item.legalName,
      isActive: item.isActive,
    }));
  }

  /** Loads the unit (and its ETag) plus its `Party` (legal name and short name, read-only). */
  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getBusinessUnit, { id });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      this.partySignal.set(await this.fetchParty(response.body.partyId));
    } catch {
      this.selectedSignal.set(null);
      this.partySignal.set(null);
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

  /** The party is display data only: if it cannot be read, its fields stay empty. */
  private async fetchParty(id: string): Promise<Party | null> {
    try {
      return await this.api.invoke(getParty, { id });
    } catch {
      return null;
    }
  }

  /** Clears `selected`/`error`/the stored `ETag` — call when entering the detail page fresh. */
  reset(): void {
    this.selectedSignal.set(null);
    this.partySignal.set(null);
    this.errorSignal.set(null);
    this.etag = null;
  }
}
