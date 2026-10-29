import { Injectable, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { createLocation } from '../../../api/platform/fn/locations/create-location';
import { getLocation } from '../../../api/platform/fn/locations/get-location';
import { listLocations } from '../../../api/platform/fn/locations/list-locations';
import { updateLocation } from '../../../api/platform/fn/locations/update-location';
import { CreateLocationRequest } from '../../../api/platform/models/create-location-request';
import { Location } from '../../../api/platform/models/location';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { UpdateLocationRequest } from '../../../api/platform/models/update-location-request';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxSortModel, Page } from '../../../shared/ui/page.model';

/**
 * Facade for the Location list/detail screens (ficha 0.4, track F). `FE-R1`
 * (`conventions-frontend.md §2`): the pages never call `HttpClient`/the generated API
 * functions directly, only this facade — which in turn only calls the
 * `ng-openapi-gen`-generated location functions, through the `Api` helper.
 *
 * Locations have no `deactivate` endpoint in the 0.4 contract
 * (`contracts/openapi/platform.yaml`): only list/create/get/update.
 */
@Injectable({ providedIn: 'root' })
export class LocationFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly selectedSignal = signal<Location | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** `ETag` of `selected()`, captured from the last `GET`/`POST`/`PUT` response; sent back as `If-Match` on `update()`. */
  private etag: string | null = null;

  /**
   * `nx-data-grid`'s `fetch` input. `page` is 0-based (grid convention, `page.model.ts`);
   * the API's `page` query param is 1-based (`conventions-api.md §4`). `GET /v1/locations`
   * has no filters in the 0.4 contract, only `page`/`pageSize`/`sort`.
   */
  list(page: number, pageSize: number, sort: NxSortModel | null): Observable<Page<LocationListItem>> {
    return from(
      this.api.invoke(listLocations, {
        page: page + 1,
        pageSize,
        sort: this.toSortParam(sort),
      }),
    ).pipe(
      map((response) => ({
        items: (response.items ?? []) as LocationListItem[],
        page: Math.max((response.page ?? 1) - 1, 0),
        pageSize: response.pageSize ?? pageSize,
        total: response.total ?? 0,
      })),
    );
  }

  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getLocation, { id });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async create(request: CreateLocationRequest): Promise<Location | null> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(createLocation, { body: request });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error));
      return null;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async update(id: string, request: UpdateLocationRequest): Promise<Location | null> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(updateLocation, {
        id,
        'If-Match': this.etag ?? undefined,
        body: request,
      });
      this.selectedSignal.set(response.body);
      this.etag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error));
      return null;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** Clears stale `selected()`/`error()` from a previous detail visit, e.g. before starting a "new" form. */
  reset(): void {
    this.selectedSignal.set(null);
    this.errorSignal.set(null);
    this.etag = null;
  }

  private toSortParam(sort: NxSortModel | null): string | undefined {
    if (!sort) {
      return undefined;
    }
    return `${sort.direction === 'desc' ? '-' : ''}${sort.colId}`;
  }

  /** Prefers the business-rule translation for `Problem.code` (e.g. 422) over the raw `detail`, per `conventions-api.md §6`. */
  private describe(error: unknown): string {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body)) {
        return body.code ? this.i18n.translateErrorCode(body.code) : body.detail ?? this.i18n.translateErrorCode(undefined);
      }
    }
    return this.i18n.translateErrorCode(undefined);
  }
}
