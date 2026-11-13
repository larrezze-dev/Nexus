import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { getLegalEntity } from '../../../api/platform/fn/legal-entities/get-legal-entity';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { getParty } from '../../../api/platform/fn/parties/get-party';
import { getLocation } from '../../../api/platform/fn/locations/get-location';
import { createLocation } from '../../../api/platform/fn/locations/create-location';
import { listLocations } from '../../../api/platform/fn/locations/list-locations';
import { updateLocation } from '../../../api/platform/fn/locations/update-location';
import { createWarehouse } from '../../../api/platform/fn/warehouses/create-warehouse';
import { deactivateWarehouse } from '../../../api/platform/fn/warehouses/deactivate-warehouse';
import { getWarehouse } from '../../../api/platform/fn/warehouses/get-warehouse';
import { listWarehouses } from '../../../api/platform/fn/warehouses/list-warehouses';
import { updateWarehouse } from '../../../api/platform/fn/warehouses/update-warehouse';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { CreateWarehouseRequest } from '../../../api/platform/models/create-warehouse-request';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { UpdateWarehouseRequest } from '../../../api/platform/models/update-warehouse-request';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { toCreateLocationRequest } from './warehouse-form';
import {
  WarehouseBusinessUnitRef,
  WarehouseFormValue,
  WarehouseLegalEntityRef,
  WarehouseLocationRef,
  WarehouseSaveError,
} from './warehouse.models';

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
  private readonly locationRefSignal = signal<WarehouseLocationRef | null>(null);
  private readonly legalEntityRefSignal = signal<WarehouseLegalEntityRef | null>(null);
  private readonly businessUnitRefSignal = signal<WarehouseBusinessUnitRef | null>(null);
  private readonly saveStepSignal = signal<'idle' | 'creatingLocation' | 'savingWarehouse'>('idle');
  private readonly pendingLocationSignal = signal<WarehouseLocationRef | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  /** `true` while `load()` runs. With `error()` set and `selected()` null the page shows «Retry» (`load(id)` again). */
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  /** Translated message of the last failed `load()`/`deactivate()`; `save()` errors are thrown, not stored here. */
  readonly error = this.errorSignal.asReadonly();
  /** Which step of a (possibly two-step) `save()` is running. */
  readonly saveStep = this.saveStepSignal.asReadonly();
  /**
   * A `Location` created by a `save()` whose second step (the warehouse) then failed. It exists
   * server-side: the UI should say so, and the next `save()` reuses it instead of creating a duplicate.
   */
  readonly pendingLocation = this.pendingLocationSignal.asReadonly();

  /** Current location/legal entity/business unit of the loaded warehouse, resolved by name (never ids). */
  readonly locationRef = this.locationRefSignal.asReadonly();
  readonly legalEntityRef = this.legalEntityRefSignal.asReadonly();
  readonly businessUnitRef = this.businessUnitRefSignal.asReadonly();

  readonly resolvedLocationLabel = computed(() => {
    const ref = this.locationRefSignal();
    return ref ? this.labelFor(ref.code, ref.name) : null;
  });
  readonly resolvedLegalEntityLabel = computed(() => {
    const ref = this.legalEntityRefSignal();
    return ref ? this.legalEntityLabel(ref) : null;
  });
  /** Reactive to the active language (reads `activeLang`). */
  readonly resolvedBusinessUnitLabel = computed(() => {
    const ref = this.businessUnitRefSignal();
    return ref ? this.businessUnitLabel(ref) : null;
  });
  /** Warehouse caption in the active language (reactive); also the breadcrumb label. `null` until loaded. */
  readonly title = computed(() => {
    const warehouse = this.selectedSignal();
    return warehouse ? resolveCaption(warehouse.captionI18n, this.i18n.activeLang(), warehouse.code) : null;
  });

  private etag: string | null = null;
  /** Monotonic sequence: only the latest `load()` (and none issued before a `reset()`) may apply its result. */
  private loadSeq = 0;

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
    const seq = ++this.loadSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.clearRefs();
    try {
      const response = await this.api.invoke$Response(getWarehouse, { id });
      if (seq !== this.loadSeq) {
        return;
      }
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      await this.resolveLabels(response.body, seq);
    } catch (error: unknown) {
      if (seq === this.loadSeq) {
        this.selectedSignal.set(null);
        this.errorSignal.set(this.describe(error).message);
      }
    } finally {
      if (seq === this.loadSeq) {
        this.loadingSignal.set(false);
      }
    }
  }

  /**
   * `create()`/`update()`/`deactivate()` do NOT catch: the `HttpErrorResponse` propagates so the
   * page can show it (use `describeError()` for the translated message / `Problem.code`).
   * `update()`/`deactivate()` send the last `ETag` as `If-Match` (409 on a stale one).
   */
  async create(request: CreateWarehouseRequest): Promise<Warehouse> {
    const response = await this.api.invoke$Response(createWarehouse, { body: request });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  async update(id: string, request: UpdateWarehouseRequest): Promise<Warehouse> {
    const response = await this.api.invoke$Response(updateWarehouse, {
      id,
      'If-Match': this.etag ?? undefined,
      body: request,
    });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  /** Propagates errors (the page asks for confirmation with `NxConfirm` first). */
  async deactivate(id: string): Promise<Warehouse> {
    this.savingSignal.set(true);
    try {
      const response = await this.api.invoke$Response(deactivateWarehouse, {
        id,
        'If-Match': this.etag ?? undefined,
      });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /**
   * Saves the form: creates (`warehouseId` null) or updates the warehouse, creating a new
   * `Location` first when the address mode is `newLocation`.
   *
   * NOT atomic, by design: the API has no composite «warehouse + location» endpoint, so a new
   * address is `POST /locations` followed by `POST|PUT /warehouses` with the returned id. If the
   * second call fails the location already exists; that is reported through
   * `WarehouseSaveError.locationCreated` / `pendingLocation()`, and calling `save()` again reuses
   * that location (updating it if the user edited the address meanwhile) so a retry never duplicates it.
   *
   * @throws WarehouseSaveError wrapping the underlying `HttpErrorResponse` (`step` says which call failed).
   */
  async save(warehouseId: string | null, value: WarehouseFormValue): Promise<Warehouse> {
    if (value.addressMode === 'sameAsLegalEntity') {
      // Needs a `party_location` API that does not exist yet; the form validator blocks this too.
      throw new Error('address mode "sameAsLegalEntity" is not available yet');
    }
    this.savingSignal.set(true);
    try {
      let locationId: string;
      if (value.addressMode === 'newLocation') {
        this.saveStepSignal.set('creatingLocation');
        try {
          locationId = await this.ensureNewLocation(value);
        } catch (error: unknown) {
          throw new WarehouseSaveError('location', error, null);
        }
      } else {
        if (!value.locationId) {
          throw new Error('locationId is required for address mode "existingLocation"');
        }
        locationId = value.locationId;
      }

      this.saveStepSignal.set('savingWarehouse');
      try {
        const warehouse = warehouseId
          ? await this.update(warehouseId, {
              locationId,
              businessUnitId: value.businessUnitId,
              code: value.code,
              captionI18n: value.captionI18n,
            })
          : await this.create({
              locationId,
              legalEntityId: value.legalEntityId,
              businessUnitId: value.businessUnitId,
              code: value.code,
              captionI18n: value.captionI18n,
            });
        this.createdLocation = null;
        this.pendingLocationSignal.set(null);
        return warehouse;
      } catch (error: unknown) {
        throw new WarehouseSaveError('warehouse', error, value.addressMode === 'newLocation' ? locationId : null);
      }
    } finally {
      this.saveStepSignal.set('idle');
      this.savingSignal.set(false);
    }
  }

  /** The location created by a previous `save()` attempt (id, ETag, and what was sent). */
  private createdLocation: { id: string; etag: string | null; fingerprint: string } | null = null;

  private async ensureNewLocation(value: WarehouseFormValue): Promise<string> {
    const body = toCreateLocationRequest(value.newLocation);
    const fingerprint = JSON.stringify(body);
    const pending = this.createdLocation;
    if (pending && pending.fingerprint === fingerprint) {
      return pending.id;
    }
    if (pending) {
      // The first attempt created the location but the user changed the address before retrying:
      // update that same location instead of creating a second one.
      const response = await this.api.invoke$Response(updateLocation, {
        id: pending.id,
        'If-Match': pending.etag ?? undefined,
        body,
      });
      this.rememberLocation(response.body.id, response.headers.get('ETag'), fingerprint, response.body);
      return pending.id;
    }
    const response = await this.api.invoke$Response(createLocation, { body });
    this.rememberLocation(response.body.id, response.headers.get('ETag'), fingerprint, response.body);
    return response.body.id;
  }

  private rememberLocation(
    id: string,
    etag: string | null,
    fingerprint: string,
    location: { code?: string | null; name: string },
  ): void {
    this.createdLocation = { id, etag, fingerprint };
    this.pendingLocationSignal.set({ id, code: location.code ?? null, name: location.name });
  }

  /** Clears stale `selected()`/error state from a previous detail visit, e.g. before starting a "new" form. */
  reset(): void {
    this.loadSeq++;
    this.loadingSignal.set(false);
    this.selectedSignal.set(null);
    this.errorSignal.set(null);
    this.clearRefs();
    this.pendingLocationSignal.set(null);
    this.createdLocation = null;
    this.etag = null;
  }

  private clearRefs(): void {
    this.locationRefSignal.set(null);
    this.legalEntityRefSignal.set(null);
    this.businessUnitRefSignal.set(null);
  }

  /**
   * `nx-entity-picker`'s `[search]` for `locationId`. `GET /v1/locations` has no `legalEntityId`
   * (nor `q`) filter in the contract and `Location` is not tied to a legal entity, so every
   * location is offered and matched client-side by code/name.
   */
  readonly searchLocations = (query: string): Observable<LocationListItem[]> =>
    from(this.api.invoke(listLocations, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' })).pipe(
      map((response) =>
        ((response.items ?? []) as LocationListItem[]).filter((item) => this.matches(query, item.code, item.name)),
      ),
    );

  /** `nx-entity-picker`'s `[search]` for `legalEntityId`: matches code and legal name. */
  readonly searchLegalEntities = (query: string): Observable<LegalEntityListItem[]> =>
    from(this.api.invoke(listLegalEntities, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' })).pipe(
      map((response) =>
        ((response.items ?? []) as LegalEntityListItem[]).filter((item) =>
          this.matches(query, item.code, item.legalName, item.displayName),
        ),
      ),
    );

  /**
   * `nx-entity-picker`'s `[search]` for `businessUnitId`, pre-filtered by the `legalEntityId`
   * currently chosen in the form. This is cosmetic convenience only (ORG-R3 is still enforced
   * server-side regardless of what this filters out): pass `null` to list every business unit.
   * Matches code and the caption in the active language.
   */
  searchBusinessUnits(legalEntityId: string | null): (query: string) => Observable<BusinessUnitListItem[]> {
    return (query: string) =>
      from(this.api.invoke(listBusinessUnits, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' })).pipe(
        map((response) =>
          ((response.items ?? []) as BusinessUnitListItem[]).filter(
            (item) =>
              (!legalEntityId || item.legalEntityId === legalEntityId) &&
              this.matches(query, item.code, this.businessUnitLabel(item)),
          ),
        ),
      );
  }

  /** `[displayWith]` for the pickers: names, never ids. The caption follows the active language. */
  readonly displayLocation = (item: { code?: string | null; name: string }): string =>
    this.labelFor(item.code, item.name);
  readonly displayLegalEntity = (item: { code: string; legalName?: string | null }): string =>
    this.legalEntityLabel(item);
  readonly displayBusinessUnit = (item: { code: string; captionI18n?: I18NText | null }): string =>
    this.businessUnitLabel(item);

  /** Translated message and `Problem.code` of an HTTP error (e.g. `ORG-R3`, shown under the business-unit field). */
  describeError(error: unknown): { message: string; code?: string } {
    return this.describe(error);
  }

  /**
   * `nx-entity-picker` is a one-shot "pick a new value" control, not a `ControlValueAccessor`:
   * it cannot be pre-populated with the warehouse's *current* values on edit. This resolves the
   * three ids into names once, right after `load()`, so the page can show them next to each picker.
   * A failed lookup only leaves that label empty (display data, not worth failing the page).
   */
  private async resolveLabels(warehouse: Warehouse, seq: number): Promise<void> {
    const [location, legalEntity, businessUnit] = await Promise.all([
      this.api.invoke(getLocation, { id: warehouse.locationId }).catch(() => null),
      this.api.invoke(getLegalEntity, { id: warehouse.legalEntityId }).catch(() => null),
      warehouse.businessUnitId
        ? this.api.invoke(getBusinessUnit, { id: warehouse.businessUnitId }).catch(() => null)
        : Promise.resolve(null),
    ]);
    // `LegalEntity` carries no legal name: it lives on its `Party` (display data only).
    const party = legalEntity ? await this.api.invoke(getParty, { id: legalEntity.partyId }).catch(() => null) : null;
    if (seq !== this.loadSeq) {
      return;
    }
    this.locationRefSignal.set(location ? { id: location.id, code: location.code, name: location.name } : null);
    this.legalEntityRefSignal.set(
      legalEntity
        ? {
            id: legalEntity.id,
            code: legalEntity.code,
            legalName: party?.legalName ?? null,
          }
        : null,
    );
    this.businessUnitRefSignal.set(
      businessUnit ? { id: businessUnit.id, code: businessUnit.code, captionI18n: businessUnit.captionI18n } : null,
    );
  }

  private legalEntityLabel(ref: { code: string; legalName?: string | null }): string {
    return ref.legalName ? `${ref.code} — ${ref.legalName}` : ref.code;
  }

  private businessUnitLabel(ref: { code: string; captionI18n?: I18NText | null }): string {
    const caption = resolveCaption(ref.captionI18n, this.i18n.activeLang(), ref.code);
    return caption === ref.code ? ref.code : `${ref.code} — ${caption}`;
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
