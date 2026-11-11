import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '../../../api/platform/api';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listAuditEvents } from '../../../api/platform/fn/audit/list-audit-events';
import { getGroup } from '../../../api/platform/fn/groups/get-group';
import { getLegalEntity } from '../../../api/platform/fn/legal-entities/get-legal-entity';
import { getLocation } from '../../../api/platform/fn/locations/get-location';
import { getParty } from '../../../api/platform/fn/parties/get-party';
import { AuditEvent } from '../../../api/platform/models/audit-event';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { flattenPayload, isUuid } from './audit-diff';

export const AUDIT_PAGE_SIZE = 50;

type RefResolver = (api: Api, id: string) => Promise<string>;

/**
 * Payload fields that point to another aggregate and how to turn the id into a short label
 * (ficha 0.8, decision 1a). Any other `*Id` uuid is displayed as a short uuid.
 */
const REF_RESOLVERS: Readonly<Partial<Record<string, RefResolver>>> = {
  locationId: async (api, id) => {
    const location = await api.invoke(getLocation, { id });
    return location.code ? `${location.code} — ${location.name}` : location.name;
  },
  legalEntityId: async (api, id) => (await api.invoke(getLegalEntity, { id })).code,
  businessUnitId: async (api, id) => (await api.invoke(getBusinessUnit, { id })).code,
  partyId: async (api, id) => {
    const party = await api.invoke(getParty, { id });
    return party.displayName ?? party.legalName;
  },
  groupId: async (api, id) => (await api.invoke(getGroup, { id })).code,
};

/**
 * Facade for `nx-audit-trail` (FE-R1). Provided at component level, never `root`, so two
 * pages never share audit state nor the resolver cache.
 */
@Injectable()
export class AuditTrailFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly itemsSignal = signal<readonly AuditEvent[]>([]);
  private readonly totalSignal = signal(0);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly resolvedLabelsSignal = signal<Readonly<Record<string, string>>>({});

  readonly items = this.itemsSignal.asReadonly();
  readonly total = this.totalSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly hasMore = computed(() => this.itemsSignal().length < this.totalSignal());
  /** `id` → label for the references that resolved; absent ids render as short uuid. */
  readonly resolvedLabels = this.resolvedLabelsSignal.asReadonly();

  private aggregateType: string | null = null;
  private aggregateId: string | null = null;
  private page = 0;
  private requestSeq = 0;
  private readonly attempted = new Set<string>();

  async load(aggregateType: string, aggregateId: string): Promise<void> {
    this.aggregateType = aggregateType;
    this.aggregateId = aggregateId;
    await this.fetchPage(1, false);
  }

  async reload(): Promise<void> {
    if (this.aggregateType !== null && this.aggregateId !== null) {
      await this.fetchPage(1, false);
    }
  }

  async loadMore(): Promise<void> {
    if (this.aggregateType !== null && this.aggregateId !== null && this.hasMore() && !this.loadingSignal()) {
      await this.fetchPage(this.page + 1, true);
    }
  }

  private async fetchPage(page: number, append: boolean): Promise<void> {
    const seq = ++this.requestSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    if (!append) {
      this.itemsSignal.set([]);
      this.totalSignal.set(0);
    }
    try {
      const response = await this.api.invoke(listAuditEvents, {
        aggregateType: this.aggregateType as string,
        aggregateId: this.aggregateId as string,
        page,
        pageSize: AUDIT_PAGE_SIZE,
      });
      if (seq !== this.requestSeq) {
        return;
      }
      const fetched = (response.items ?? []) as AuditEvent[];
      this.page = page;
      this.itemsSignal.update((current) => (append ? [...current, ...fetched] : fetched));
      this.totalSignal.set(response.total ?? this.itemsSignal().length);
      this.loadingSignal.set(false);
      void this.resolveReferences(fetched);
    } catch (error: unknown) {
      if (seq !== this.requestSeq) {
        return;
      }
      this.errorSignal.set(this.describe(error));
      this.loadingSignal.set(false);
    }
  }

  /** Lazily resolves each distinct `(field, id)` once per facade instance; failures stay unresolved. */
  private async resolveReferences(events: readonly AuditEvent[]): Promise<void> {
    const pending = new Map<string, { field: string; id: string }>();
    for (const event of events) {
      for (const payload of [event.payload, event.previousPayload]) {
        for (const [path, value] of flattenPayload(payload)) {
          const resolver = REF_RESOLVERS[path];
          if (resolver && isUuid(value) && !this.attempted.has(value)) {
            pending.set(value, { field: path, id: value });
          }
        }
      }
    }
    await Promise.all(
      [...pending.values()].map(async ({ field, id }) => {
        this.attempted.add(id);
        try {
          const label = await (REF_RESOLVERS[field] as RefResolver)(this.api, id);
          this.resolvedLabelsSignal.update((current) => ({ ...current, [id]: label }));
        } catch {
          // 403/404/network: the timeline keeps the short uuid for this reference.
        }
      }),
    );
  }

  private describe(error: unknown): string {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body)) {
        return body.code
          ? this.i18n.translateErrorCode(body.code)
          : body.detail ?? this.i18n.translateErrorCode(undefined);
      }
    }
    return this.i18n.translateErrorCode(undefined);
  }
}
