import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { createRoleAssignment } from '../../../api/platform/fn/role-assignments/create-role-assignment';
import { getRoleAssignment } from '../../../api/platform/fn/role-assignments/get-role-assignment';
import { getUserEffectiveScope } from '../../../api/platform/fn/role-assignments/get-user-effective-scope';
import { listRoleAssignments } from '../../../api/platform/fn/role-assignments/list-role-assignments';
import { revokeRoleAssignment } from '../../../api/platform/fn/role-assignments/revoke-role-assignment';
import { listRoles } from '../../../api/platform/fn/roles/list-roles';
import { listWarehouses } from '../../../api/platform/fn/warehouses/list-warehouses';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import type { CreateRoleAssignmentRequest } from '../../../api/platform/models/create-role-assignment-request';
import type { EffectiveScope } from '../../../api/platform/models/effective-scope';
import type { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import type { RoleAssignment } from '../../../api/platform/models/role-assignment';
import type { RoleAssignmentListItem } from '../../../api/platform/models/role-assignment-list-item';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import type { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import {
  isConcurrencyConflict,
  isDuplicateConflict,
  localizedFieldErrors,
  problemOf,
} from '../../../core/http/problem';
import { IdempotentCreateId } from '../../../core/ids/uuid-v7';
import { NxFormatService } from '../../../core/i18n/nx-format.service';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';

/** One assignment of a user as the screen shows it: names, never ids. */
export interface AssignmentRow {
  readonly id: string;
  /** Role caption in the active language (the code if it has none). */
  readonly roleLabel: string;
  readonly scopeType: RoleAssignmentListItem['scopeType'];
  /** Entity name for the scope; `null` for `GLOBAL`; «—» when the entity could not be read. */
  readonly scopeLabel: string | null;
  /** «1 Jan 2026». */
  readonly validFromLabel: string;
  /** Last day (inclusive) as «31 Dec 2026»; `null` = no end. */
  readonly validToLabel: string | null;
  readonly source: RoleAssignmentListItem['source'];
  /** Valid now (started, not ended). */
  readonly isActive: boolean;
  /** MANUAL, still valid: the only ones the UI offers to revoke (SEED is protected by ACC-R6). */
  readonly canRevoke: boolean;
}

/** How a failed grant/revoke is to be shown (see `describeError`). */
export interface AssignmentErrorInfo {
  /** `concurrency`: the interceptor's dialog already ran, show nothing. */
  readonly kind: 'concurrency' | 'validation' | 'rule' | 'overlap' | 'generic';
  /** Translated alert text; `null` when there is nothing to say above the form. */
  readonly message: string | null;
  /** Form control name → translated messages, for the fields to mark. */
  readonly fields: Readonly<Record<string, readonly string[]>>;
}

/** `GET /v1/*` lists of reference data have no `q` (except roles/warehouses): one page, filtered client-side. */
const REFERENCE_PAGE_SIZE = 100;
const DAY_MS = 86_400_000;

/**
 * Facade for `RoleAssignment` (`ACC-R*`, `domain/access.md`): the only thing between the
 * «Assignments» tab on the user detail / the «What does this user see?» screen and the API.
 * Scoped to one user at a time (`list(userId)`): a user has a handful of assignments, so there is
 * no paging UI.
 *
 * `rows()` resolves role (caption in the active language), scope (legal entity / business unit /
 * warehouse by name, `GLOBAL` has none) and validity; it follows a language switch. `revoke()`
 * reads the assignment first for its `ETag` (`GET /v1/role-assignments/{id}`), so any row can be
 * revoked, not only the ones created in this browser session. `create()`/`revoke()` do NOT
 * catch: the page asks `describeError()` how to show the failure.
 *
 * Validity end: `valid_to` is exclusive (`> now()`), and the form's «until» day is inclusive, so
 * the form sends the next UTC midnight and `validToLabel` shows the day before an exact midnight.
 */
@Injectable({ providedIn: 'root' })
export class RoleAssignmentsFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly format = inject(NxFormatService);

  private readonly itemsSignal = signal<RoleAssignmentListItem[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly savingSignal = signal(false);
  private readonly rolesSignal = signal<ReadonlyMap<string, RoleListItem>>(new Map());
  private readonly legalEntitiesSignal = signal<ReadonlyMap<string, LegalEntityListItem>>(new Map());
  private readonly businessUnitsSignal = signal<ReadonlyMap<string, BusinessUnitListItem>>(new Map());
  private readonly warehousesSignal = signal<ReadonlyMap<string, WarehouseListItem>>(new Map());

  /** Raw items of the last `list()`. */
  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  /** Translated message of the last failed `list()`. */
  readonly error = this.errorSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();

  /** Items of the last `list()` with names resolved; reactive to the active language. */
  readonly rows = computed<AssignmentRow[]>(() => {
    const lang = this.i18n.activeLang();
    const roles = this.rolesSignal();
    const now = Date.now();
    return this.itemsSignal().map((item) => {
      const role = roles.get(item.roleId);
      const validTo = item.validTo ? new Date(item.validTo).getTime() : null;
      const isActive = new Date(item.validFrom).getTime() <= now && (validTo === null || validTo > now);
      return {
        id: item.id,
        roleLabel: role ? resolveCaption(role.captionI18n, lang, role.code) : '—',
        scopeType: item.scopeType,
        scopeLabel: this.scopeLabel(item),
        validFromLabel: this.format.formatDateMedium(item.validFrom),
        validToLabel: item.validTo ? this.format.formatDateMedium(this.lastDay(item.validTo)) : null,
        source: item.source,
        isActive,
        canRevoke: item.source === 'MANUAL' && isActive,
      };
    });
  });

  private readonly createId = new IdempotentCreateId();
  /** Monotonic sequence: only the latest `list()` (and none issued before a `reset()`) may apply its result. */
  private loadSeq = 0;

  /** Forgets the loaded user's assignments (e.g. when the page opens another user). */
  reset(): void {
    this.loadSeq++;
    this.itemsSignal.set([]);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }

  /** Loads every assignment for `userId` plus the names they reference. */
  async list(userId: string): Promise<RoleAssignmentListItem[]> {
    const seq = ++this.loadSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const [response, roles, legalEntities, businessUnits, warehouses] = await Promise.allSettled([
        this.api.invoke(listRoleAssignments, { userId, pageSize: 200, sort: '-validFrom' }),
        this.api.invoke(listRoles, { page: 1, pageSize: 200, sort: 'code' }),
        this.api.invoke(listLegalEntities, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' }),
        this.api.invoke(listBusinessUnits, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' }),
        this.api.invoke(listWarehouses, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' }),
      ]);
      if (seq !== this.loadSeq) {
        return [];
      }
      if (response.status === 'rejected') {
        throw response.reason;
      }
      const items = response.value.items ?? [];
      // A failed name lookup leaves «—» for those rows instead of failing the whole tab.
      this.rolesSignal.set(this.byId(roles, (r) => r.items));
      this.legalEntitiesSignal.set(this.byId(legalEntities, (r) => r.items));
      this.businessUnitsSignal.set(this.byId(businessUnits, (r) => r.items));
      this.warehousesSignal.set(this.byId(warehouses, (r) => r.items));
      this.itemsSignal.set(items);
      return items;
    } catch {
      if (seq !== this.loadSeq) {
        return [];
      }
      this.itemsSignal.set([]);
      this.errorSignal.set(this.transloco.translate('errors.generic'));
      return [];
    } finally {
      if (seq === this.loadSeq) {
        this.loadingSignal.set(false);
      }
    }
  }

  /** Propagates errors; a retry of the same request reuses its `id` (the server answers with the existing one). */
  async create(request: CreateRoleAssignmentRequest): Promise<RoleAssignment> {
    this.savingSignal.set(true);
    try {
      const response = await this.api.invoke$Response(createRoleAssignment, {
        body: { ...request, id: request.id ?? this.createId.idFor(request) },
      });
      this.createId.clear();
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /** Reads the assignment for its `ETag`, then revokes it (ACC-R3). Propagates errors. */
  async revoke(id: string): Promise<RoleAssignment> {
    this.savingSignal.set(true);
    try {
      const current = await this.api.invoke$Response(getRoleAssignment, { id });
      const response = await this.api.invoke$Response(revokeRoleAssignment, {
        id,
        'If-Match': current.headers.get('ETag') ?? undefined,
      });
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async getEffectiveScope(userId: string): Promise<EffectiveScope> {
    return this.api.invoke(getUserEffectiveScope, { id: userId });
  }

  /**
   * How to show a failed `create()`/`revoke()`:
   * - 409 `CONCURRENCY`: nothing (the interceptor's dialog ran);
   * - 409 `CONFLICT`: overlapping validity for the same role and scope;
   * - 400: `errors.validation` + the named fields marked (`roleId` → `role`, `scopeEntityId` → `scopeEntity`);
   * - 422 `ACC-R1`/`ACC-R2`: the text under the scope-entity field; other `ACC-R*`: the alert;
   * - anything else: the generic message.
   */
  describeError(error: unknown): AssignmentErrorInfo {
    if (isConcurrencyConflict(error)) {
      return { kind: 'concurrency', message: null, fields: {} };
    }
    if (isDuplicateConflict(error)) {
      return { kind: 'overlap', message: this.transloco.translate('org.users.detail.assignments.errors.overlap'), fields: {} };
    }
    const status = (error as { status?: number } | null)?.status;
    const problem = problemOf(error);
    if (status === 400) {
      const raw = localizedFieldErrors(problem?.errors, this.transloco.translate('shared.form.invalidValue'));
      const fields: Record<string, readonly string[]> = {};
      for (const [field, messages] of Object.entries(raw)) {
        fields[FIELD_ALIASES[field] ?? field] = messages;
      }
      return { kind: 'validation', message: this.transloco.translate('errors.validation'), fields };
    }
    if (status === 422 && problem?.code) {
      const message = this.i18n.translateErrorCode(problem.code);
      if (problem.code === 'ACC-R1' || problem.code === 'ACC-R2') {
        return { kind: 'rule', message: null, fields: { scopeEntity: [message] } };
      }
      return { kind: 'rule', message, fields: {} };
    }
    return { kind: 'generic', message: this.i18n.translateErrorCode(problem?.code), fields: {} };
  }

  // --- labels for the form's pickers: names, never ids ---------------------------------------

  readonly displayRole = (role: RoleListItem): string =>
    `${role.code} — ${resolveCaption(role.captionI18n, this.i18n.getActiveLanguage(), role.code)}`;
  readonly displayLegalEntity = (le: { code: string; legalName: string }): string => `${le.code} — ${le.legalName}`;
  readonly displayBusinessUnit = (bu: BusinessUnitListItem): string =>
    `${bu.code} — ${resolveCaption(bu.captionI18n, this.i18n.getActiveLanguage(), bu.code)}`;
  readonly displayWarehouse = (wh: WarehouseListItem): string =>
    `${wh.code} — ${resolveCaption(wh.captionI18n, this.i18n.getActiveLanguage(), wh.code)}`;

  /**
   * Remote-search for the role picker. `GET /v1/roles` has a `q` free-text query param in the
   * contract, so this filters server-side.
   */
  searchRoles = (query: string): Observable<RoleListItem[]> => {
    const q = query.trim();
    return from(
      this.api.invoke(listRoles, { page: 1, pageSize: 100, sort: 'code', q: q || undefined }),
    ).pipe(map((response) => response.items ?? []));
  };

  /** `GET /v1/legal-entities` has no `q`: one page sorted by `code`, filtered client-side on code and legal name. */
  searchLegalEntities = (query: string): Observable<LegalEntityListItem[]> => {
    return from(this.api.invoke(listLegalEntities, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' })).pipe(
      map((response) => {
        const needle = query.trim().toLowerCase();
        const items = response.items ?? [];
        return needle
          ? items.filter(
              (le) => le.code.toLowerCase().includes(needle) || le.legalName.toLowerCase().includes(needle),
            )
          : items;
      }),
    );
  };

  /** `GET /v1/business-units` has no `q` either: same client-side filter on code and caption. */
  searchBusinessUnits = (query: string): Observable<BusinessUnitListItem[]> => {
    return from(this.api.invoke(listBusinessUnits, { page: 1, pageSize: REFERENCE_PAGE_SIZE, sort: 'code' })).pipe(
      map((response) => {
        const needle = query.trim().toLowerCase();
        const items = response.items ?? [];
        return needle ? items.filter((bu) => this.displayBusinessUnit(bu).toLowerCase().includes(needle)) : items;
      }),
    );
  };

  /** `GET /v1/warehouses` has a `q` query parameter: filtered server-side. */
  searchWarehouses = (query: string): Observable<WarehouseListItem[]> => {
    const q = query.trim();
    return from(this.api.invoke(listWarehouses, { page: 1, pageSize: 100, sort: 'code', q: q || undefined })).pipe(
      map((response) => response.items ?? []),
    );
  };

  private scopeLabel(item: RoleAssignmentListItem): string | null {
    if (item.scopeType === 'GLOBAL' || !item.scopeEntityId) {
      return null;
    }
    const id = item.scopeEntityId;
    switch (item.scopeType) {
      case 'LEGAL_ENTITY': {
        const le = this.legalEntitiesSignal().get(id);
        return le ? le.legalName : '—';
      }
      case 'BUSINESS_UNIT': {
        const bu = this.businessUnitsSignal().get(id);
        return bu ? resolveCaption(bu.captionI18n, this.i18n.activeLang(), bu.code) : '—';
      }
      case 'WAREHOUSE': {
        const wh = this.warehousesSignal().get(id);
        return wh ? resolveCaption(wh.captionI18n, this.i18n.activeLang(), wh.code) : '—';
      }
      default:
        return '—';
    }
  }

  /** `valid_to` is exclusive: an exact UTC midnight means «until the day before». */
  private lastDay(validTo: string): string {
    const time = new Date(validTo).getTime();
    return Number.isNaN(time) || time % DAY_MS !== 0 ? validTo : new Date(time - DAY_MS).toISOString();
  }

  private byId<T extends { id: string }, R>(
    result: PromiseSettledResult<R>,
    pick: (response: R) => T[] | undefined,
  ): ReadonlyMap<string, T> {
    if (result.status !== 'fulfilled') {
      return new Map();
    }
    return new Map((pick(result.value) ?? []).map((item) => [item.id, item]));
  }
}

/** Server field name → form control name. */
const FIELD_ALIASES: Readonly<Record<string, string>> = {
  roleId: 'role',
  scopeEntityId: 'scopeEntity',
};
