import { Injectable, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { createRoleAssignment } from '../../../api/platform/fn/role-assignments/create-role-assignment';
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
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';

/**
 * Facade for `RoleAssignment` (`ACC-R*`, `domain/access.md`): the only thing between the
 * "Asignaciones" tab on the user detail page / the "¿Qué ve este usuario?" screen and the
 * `platform` API client. Deliberately scoped to a single user at a time (`list(userId)`), not
 * a generic paginated listing: a user realistically has a handful of assignments, so there is
 * no grid/paging UI on top of this.
 *
 * **Known contract gap, not a bug — flag back to Gate C**: `RoleAssignmentListItem` (what
 * `list()` returns) carries no `ETag`/version, and there is no `GET /v1/role-assignments/{id}`
 * to fetch one for an assignment the UI didn't just create itself. `revoke()` needs `If-Match`
 * (`conventions-api.md §5`), so this facade can only safely revoke an assignment whose `ETag`
 * it captured locally from its own `create()` response, in this same browser session —
 * tracked in `etagsByAssignmentId`. For every other row (seeded, created in a previous page
 * load, created by someone else) there is no sanctioned way to obtain a valid `If-Match`, and
 * this facade does NOT invent one (no `'*'`, no guessing). `canRevoke(id)` tells the caller
 * which rows it may offer a "Revocar" action for; the rest show none.
 */
@Injectable({ providedIn: 'root' })
export class RoleAssignmentsFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly itemsSignal = signal<RoleAssignmentListItem[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** See the contract-gap note on the class doc comment. */
  private readonly etagsByAssignmentId = new Map<string, string>();

  /** Loads and stores every assignment for `userId` (no pagination UI, see class doc comment). */
  async list(userId: string): Promise<RoleAssignmentListItem[]> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke(listRoleAssignments, {
        userId,
        pageSize: 200,
        sort: '-validFrom',
      });
      const items = response.items ?? [];
      this.itemsSignal.set(items);
      return items;
    } catch {
      this.itemsSignal.set([]);
      this.errorSignal.set('load_failed');
      return [];
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** Whether `id` has an `ETag` this facade captured itself (see the contract-gap note above). */
  canRevoke(id: string): boolean {
    return this.etagsByAssignmentId.has(id);
  }

  async create(request: CreateRoleAssignmentRequest): Promise<RoleAssignment> {
    const response = await this.api.invoke$Response(createRoleAssignment, { body: request });
    const etag = response.headers.get('ETag');
    if (etag) {
      this.etagsByAssignmentId.set(response.body.id, etag);
    }
    return response.body;
  }

  /** Only call when `canRevoke(id)` is true (see the contract-gap note above). */
  async revoke(id: string): Promise<RoleAssignment> {
    const etag = this.etagsByAssignmentId.get(id);
    const response = await this.api.invoke$Response(revokeRoleAssignment, {
      id,
      'If-Match': etag ?? undefined,
    });
    this.etagsByAssignmentId.delete(id);
    return response.body;
  }

  async getEffectiveScope(userId: string): Promise<EffectiveScope> {
    return this.api.invoke(getUserEffectiveScope, { id: userId });
  }

  /**
   * Remote-search for the role picker. `GET /v1/roles` has a `q` free-text query param in the
   * contract, unlike `legal-entities`/`business-units`/`warehouses` below, so this filters
   * server-side instead of fetching a page and filtering client-side.
   */
  searchRoles = (query: string): Observable<RoleListItem[]> => {
    const q = query.trim();
    return from(
      this.api.invoke(listRoles, { page: 1, pageSize: 100, sort: 'code', q: q || undefined }),
    ).pipe(map((response) => response.items ?? []));
  };

  /**
   * Remote-search for the `LEGAL_ENTITY` scope picker. `GET /v1/legal-entities` has no `q`
   * query parameter (only `page`/`pageSize`/`sort`), so — same pattern as
   * `LegalEntitiesFacade.search()` — this fetches one page sorted by `code` and filters
   * client-side on a substring match of `code`.
   */
  searchLegalEntities = (query: string): Observable<LegalEntityListItem[]> => {
    return from(this.api.invoke(listLegalEntities, { page: 1, pageSize: 100, sort: 'code' })).pipe(
      map((response) => {
        const needle = query.trim().toLowerCase();
        const items = response.items ?? [];
        return needle ? items.filter((le) => le.code.toLowerCase().includes(needle)) : items;
      }),
    );
  };

  /**
   * Remote-search for the `BUSINESS_UNIT` scope picker. `GET /v1/business-units` has no `q`
   * query parameter either — same client-side substring-filter pattern as
   * `UsersFacade.searchBusinessUnits()`.
   */
  searchBusinessUnits = (query: string): Observable<BusinessUnitListItem[]> => {
    return from(this.api.invoke(listBusinessUnits, { page: 1, pageSize: 100, sort: 'code' })).pipe(
      map((response) => {
        const needle = query.trim().toLowerCase();
        const items = response.items ?? [];
        return needle
          ? items.filter(
              (bu) => bu.code.toLowerCase().includes(needle) || resolveCaption(bu.captionI18n, this.i18n.getActiveLanguage(), bu.code).toLowerCase().includes(needle),
            )
          : items;
      }),
    );
  };

  /**
   * Remote-search for the `WAREHOUSE` scope picker. `GET /v1/warehouses` does have a `q` query
   * parameter, so this filters server-side.
   */
  searchWarehouses = (query: string): Observable<WarehouseListItem[]> => {
    const q = query.trim();
    return from(this.api.invoke(listWarehouses, { page: 1, pageSize: 100, sort: 'code', q: q || undefined })).pipe(
      map((response) => response.items ?? []),
    );
  };
}
