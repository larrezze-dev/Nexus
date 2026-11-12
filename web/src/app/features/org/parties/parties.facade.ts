import { Injectable, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { createParty } from '../../../api/platform/fn/parties/create-party';
import { deactivateParty } from '../../../api/platform/fn/parties/deactivate-party';
import { getParty } from '../../../api/platform/fn/parties/get-party';
import { listParties, type ListParties$Params } from '../../../api/platform/fn/parties/list-parties';
import { updateParty } from '../../../api/platform/fn/parties/update-party';
import type { CreatePartyRequest } from '../../../api/platform/models/create-party-request';
import type { Party } from '../../../api/platform/models/party';
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import type { UpdatePartyRequest } from '../../../api/platform/models/update-party-request';
import { isProblem, type Problem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import type { NxSortModel, Page } from '../../../shared/ui/page.model';

/** Filters `GET /v1/parties` supports in the step 0.4 contract, passed through `nx-data-grid`'s `filters` input. */
export interface PartyFilters {
  readonly q?: string;
  readonly partyType?: 'ORGANIZATION' | 'PERSON';
  readonly isInternal?: boolean;
}

/**
 * Facade for the Parties directory screens (`FE-R1`): the list/detail pages never call
 * `HttpClient` or the generated API functions directly, only this facade.
 *
 * Like `LegalEntitiesFacade`, `create()`/`update()`/`deactivate()` do NOT catch errors: the raw
 * `HttpErrorResponse` reaches the detail page, which turns a 400 into per-field messages
 * (`Problem.errors`) and a 422 into the translated business-rule message (`Problem.code`).
 * `errorInterceptor` already handles 401/403/404 and the 409 concurrency dialog. `load()` keeps
 * catching: a failed load shows the page's error state with «Retry».
 */
@Injectable({ providedIn: 'root' })
export class PartiesFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly selectedSignal = signal<Party | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  private selectedETag: string | null = null;

  /** `fetch` for `nx-data-grid`. `page` is 0-based (grid convention); the API itself is 1-based. */
  list(
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
    filters: Record<string, unknown>,
  ): Observable<Page<PartyListItem>> {
    const typed = filters as PartyFilters;
    const params: ListParties$Params = {
      page: page + 1,
      pageSize,
      sort: sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined,
      q: typed.q || undefined,
      partyType: typed.partyType,
      isInternal: typed.isInternal,
    };
    return from(this.api.invoke(listParties, params)).pipe(
      map((response) => ({
        items: response.items ?? [],
        page,
        pageSize,
        total: response.total,
      })),
    );
  }

  reset(): void {
    this.selectedSignal.set(null);
    this.selectedETag = null;
    this.errorSignal.set(null);
  }

  /** Loads the party and its ETag; on failure `error` is set and `selected` cleared (retry state). */
  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getParty, { id });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
    } catch (error: unknown) {
      this.selectedSignal.set(null);
      this.selectedETag = null;
      this.applyError(error);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** Creates a party. Errors propagate (the page maps 400 to fields and 422 to a message). */
  async create(request: CreatePartyRequest): Promise<Party> {
    const response = await this.api.invoke$Response(createParty, { body: request });
    this.selectedSignal.set(response.body);
    this.selectedETag = response.headers.get('ETag');
    return response.body;
  }

  /** Saves a party with `If-Match` = the last known ETag. Errors propagate. */
  async update(id: string, request: UpdatePartyRequest): Promise<Party> {
    const response = await this.api.invoke$Response(updateParty, {
      id,
      'If-Match': this.selectedETag ?? undefined,
      body: request,
    });
    this.selectedSignal.set(response.body);
    this.selectedETag = response.headers.get('ETag');
    return response.body;
  }

  /** Deactivates a party with `If-Match` = the last known ETag. Errors propagate. */
  async deactivate(id: string): Promise<Party> {
    const response = await this.api.invoke$Response(deactivateParty, {
      id,
      'If-Match': this.selectedETag ?? undefined,
    });
    this.selectedSignal.set(response.body);
    this.selectedETag = response.headers.get('ETag');
    return response.body;
  }

  private applyError(error: unknown): void {
    const problem = this.extractProblem(error);
    this.errorSignal.set(problem?.detail ?? this.i18n.translateErrorCode(problem?.code));
  }

  private extractProblem(error: unknown): Problem | undefined {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body)) {
        return body;
      }
    }
    return undefined;
  }
}
