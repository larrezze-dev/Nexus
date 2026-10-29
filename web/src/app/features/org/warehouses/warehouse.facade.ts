import { Injectable, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { getLegalEntity } from '../../../api/platform/fn/legal-entities/get-legal-entity';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { getLocation } from '../../../api/platform/fn/locations/get-location';
import { listLocations } from '../../../api/platform/fn/locations/list-locations';
import { createWarehouse } from '../../../api/platform/fn/warehouses/create-warehouse';
import { deactivateWarehouse } from '../../../api/platform/fn/warehouses/deactivate-warehouse';
import { getWarehouse } from '../../../api/platform/fn/warehouses/get-warehouse';
import { listWarehouses } from '../../../api/platform/fn/warehouses/list-warehouses';
import { updateWarehouse } from '../../../api/platform/fn/warehouses/update-warehouse';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { CreateWarehouseRequest } from '../../../api/platform/models/create-warehouse-request';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { UpdateWarehouseRequest } from '../../../api/platform/models/update-warehouse-request';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxSortModel, Page } from '../../../shared/ui/page.model';

/** `GET /v1/warehouses` filters (`domain/org.md §5`, `contracts/openapi/platform.yaml`). */
export interface WarehouseListFilters {
  readonly legalEntityId?: string;
  readonly businessUnitId?: string;
  readonly isActive?: boolean;
  readonly q?: string;
}

/**
 * `LegalEntity`/`BusinessUnit`/`Location` have no `q` filter on their `GET` list endpoints in
 * the 0.4 contract: `nx-entity-picker`'s searches fetch up to this many reference rows and
 * filter client-side. Reference data (legal entities, business units, locations) stays in the
 * "a few hundred rows" range for the foreseeable future (`conventions-api.md §4`), so this is a
 * reasonable one-shot page rather than real server-side search.
 */
const REFERENCE_PAGE_SIZE = 100;

/**
 * Facade for the Warehouse list/detail screens (ficha 0.4, track F). `FE-R1`
 * (`conventions-frontend.md §2`): the pages never call `HttpClient`/the generated API
 * functions directly, only this facade.
 */
@Injectable({ providedIn: 'root' })
export class WarehouseFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly selectedSignal = signal<Warehouse | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly savingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly businessUnitErrorSignal = signal<string | null>(null);
  private readonly resolvedLocationLabelSignal = signal<string | null>(null);
  private readonly resolvedLegalEntityLabelSignal = signal<string | null>(null);
  private readonly resolvedBusinessUnitLabelSignal = signal<string | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  /**
   * ORG-R3 ("a Warehouse with a `businessUnitId` requires that BU to belong to the same
   * `legalEntityId`"), surfaced separately from the generic `error()` so the detail page can
   * show it right under the `businessUnitId` field, not just a generic toast (ficha 0.4 Gate D).
   */
  readonly businessUnitError = this.businessUnitErrorSignal.asReadonly();
  readonly resolvedLocationLabel = this.resolvedLocationLabelSignal.asReadonly();
  readonly resolvedLegalEntityLabel = this.resolvedLegalEntityLabelSignal.asReadonly();
  readonly resolvedBusinessUnitLabel = this.resolvedBusinessUnitLabelSignal.asReadonly();

  private etag: string | null = null;

  list(
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: WarehouseListFilters,
  ): Observable<Page<WarehouseListItem>> {
    return from(
      this.api.invoke(listWarehouses, {
        page: page + 1,
        pageSize,
        sort: this.toSortParam(sort),
        legalEntityId: filters.legalEntityId,
        businessUnitId: filters.businessUnitId,
        isActive: filters.isActive,
        q: filters.q,
      }),
    ).pipe(
      map((response) => ({
        items: (response.items ?? []) as WarehouseListItem[],
        page: Math.max((response.page ?? 1) - 1, 0),
        pageSize: response.pageSize ?? pageSize,
        total: response.total ?? 0,
      })),
    );
  }

  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.resolvedLocationLabelSignal.set(null);
    this.resolvedLegalEntityLabelSignal.set(null);
    this.resolvedBusinessUnitLabelSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getWarehouse, { id });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      await this.resolveLabels(response.body);
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error).message);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async create(request: CreateWarehouseRequest): Promise<Warehouse | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    this.businessUnitErrorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(createWarehouse, { body: request });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.applyError(error);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async update(id: string, request: UpdateWarehouseRequest): Promise<Warehouse | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    this.businessUnitErrorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(updateWarehouse, {
        id,
        'If-Match': this.etag ?? undefined,
        body: request,
      });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.applyError(error);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async deactivate(id: string): Promise<Warehouse | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(deactivateWarehouse, {
        id,
        'If-Match': this.etag ?? undefined,
      });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error).message);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /** Clears stale `selected()`/error state from a previous detail visit, e.g. before starting a "new" form. */
  reset(): void {
    this.selectedSignal.set(null);
    this.errorSignal.set(null);
    this.businessUnitErrorSignal.set(null);
    this.resolvedLocationLabelSignal.set(null);
    this.resolvedLegalEntityLabelSignal.set(null);
    this.resolvedBusinessUnitLabelSignal.set(null);
    this.etag = null;
  }

  /** `nx-entity-picker`'s `[search]` for `locationId`. */
  readonly searchLocations = (query: string): Observable<LocationListItem[]> =>
    from(this.api.invoke(listLocations, { page: 1, pageSize: REFERENCE_PAGE_SIZE })).pipe(
      map((response) =>
        ((response.items ?? []) as LocationListItem[]).filter((item) => this.matches(query, item.code, item.name)),
      ),
    );

  /** `nx-entity-picker`'s `[search]` for `legalEntityId`. */
  readonly searchLegalEntities = (query: string): Observable<LegalEntityListItem[]> =>
    from(this.api.invoke(listLegalEntities, { page: 1, pageSize: REFERENCE_PAGE_SIZE })).pipe(
      map((response) => ((response.items ?? []) as LegalEntityListItem[]).filter((item) => this.matches(query, item.code))),
    );

  /**
   * `nx-entity-picker`'s `[search]` for `businessUnitId`, pre-filtered by the `legalEntityId`
   * currently chosen in the form. This is cosmetic convenience only (ORG-R3 is still enforced
   * server-side regardless of what this filters out): pass `null` to list every business unit.
   */
  searchBusinessUnits(legalEntityId: string | null): (query: string) => Observable<BusinessUnitListItem[]> {
    return (query: string) =>
      from(this.api.invoke(listBusinessUnits, { page: 1, pageSize: REFERENCE_PAGE_SIZE })).pipe(
        map((response) =>
          ((response.items ?? []) as BusinessUnitListItem[]).filter(
            (item) => (!legalEntityId || item.legalEntityId === legalEntityId) && this.matches(query, item.code),
          ),
        ),
      );
  }

  /**
   * `nx-entity-picker` is a one-shot "pick a new value" control, not a `ControlValueAccessor`
   * (`nx-entity-picker.component.ts`'s own doc comment): it cannot be pre-populated with the
   * warehouse's *current* `locationId`/`legalEntityId`/`businessUnitId` on edit. This resolves
   * those three ids into short display labels once, right after `load()`, so the detail page can
   * show "Current: <label>" next to each picker.
   */
  private async resolveLabels(warehouse: Warehouse): Promise<void> {
    const [location, legalEntity, businessUnit] = await Promise.all([
      this.api.invoke(getLocation, { id: warehouse.locationId }).catch(() => null),
      this.api.invoke(getLegalEntity, { id: warehouse.legalEntityId }).catch(() => null),
      warehouse.businessUnitId
        ? this.api.invoke(getBusinessUnit, { id: warehouse.businessUnitId }).catch(() => null)
        : Promise.resolve(null),
    ]);
    this.resolvedLocationLabelSignal.set(location ? this.labelFor(location.code, location.name) : null);
    this.resolvedLegalEntityLabelSignal.set(legalEntity ? legalEntity.code : null);
    this.resolvedBusinessUnitLabelSignal.set(businessUnit ? businessUnit.code : null);
  }

  private labelFor(code: string | null | undefined, name: string): string {
    return code ? `${code} — ${name}` : name;
  }

  private matches(query: string, ...fields: (string | null | undefined)[]): boolean {
    const needle = query.trim().toLowerCase();
    if (!needle) {
      return true;
    }
    return fields.some((field) => !!field && field.toLowerCase().includes(needle));
  }

  private toSortParam(sort: NxSortModel | null): string | undefined {
    if (!sort) {
      return undefined;
    }
    return `${sort.direction === 'desc' ? '-' : ''}${sort.colId}`;
  }

  private applyError(error: unknown): void {
    const described = this.describe(error);
    if (described.code === 'ORG-R3') {
      this.businessUnitErrorSignal.set(described.message);
    } else {
      this.errorSignal.set(described.message);
    }
  }

  /** Prefers the business-rule translation for `Problem.code` (e.g. `ORG-R3`) over the raw `detail`. */
  private describe(error: unknown): { message: string; code?: string } {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body)) {
        const message = body.code
          ? this.i18n.translateErrorCode(body.code)
          : body.detail ?? this.i18n.translateErrorCode(undefined);
        return { message, code: body.code };
      }
    }
    return { message: this.i18n.translateErrorCode(undefined) };
  }
}
