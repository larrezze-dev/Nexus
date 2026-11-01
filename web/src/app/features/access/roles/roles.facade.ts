import { Injectable, inject, signal } from '@angular/core';
import { Observable, from } from 'rxjs';
import { map } from 'rxjs/operators';
import { Api } from '../../../api/platform/api';
import { createRole } from '../../../api/platform/fn/roles/create-role';
import { deactivateRole } from '../../../api/platform/fn/roles/deactivate-role';
import { getRole } from '../../../api/platform/fn/roles/get-role';
import { getRolePermissions } from '../../../api/platform/fn/roles/get-role-permissions';
import { listPermissions } from '../../../api/platform/fn/roles/list-permissions';
import { listRoles } from '../../../api/platform/fn/roles/list-roles';
import { updateRole } from '../../../api/platform/fn/roles/update-role';
import { updateRolePermissions } from '../../../api/platform/fn/roles/update-role-permissions';
import { CreateRoleRequest } from '../../../api/platform/models/create-role-request';
import { Permission } from '../../../api/platform/models/permission';
import { PermissionGrant } from '../../../api/platform/models/permission-grant';
import { Role } from '../../../api/platform/models/role';
import { RoleListItem } from '../../../api/platform/models/role-list-item';
import { UpdateRoleRequest } from '../../../api/platform/models/update-role-request';
import { NxSortModel, Page } from '../../../shared/ui/page.model';

/**
 * Facade for `Role` master data and its permission grants (`ACC-R7`/`ACC-R9`,
 * `access.md §4`): the only thing between `roles/*` pages and the `platform`
 * API client. Same `selected`/`loading`/`error` signal + `ETag` pattern as
 * `LegalEntitiesFacade` for the `Role` resource itself (`create`/`update`/
 * `deactivate` send `If-Match`).
 *
 * `updateRolePermissions` has no `If-Match` in the contract (`ACC-R7`: the
 * server computes the add/remove diff from the full desired list, it is not
 * a versioned-row PUT), so `savePermissions()` does not track an `ETag`.
 *
 * `create()`/`update()`/`deactivate()`/`savePermissions()` deliberately do
 * NOT catch errors: they let the raw `HttpErrorResponse` propagate to the
 * calling detail page, same hand-off as `LegalEntitiesFacade`.
 */
@Injectable({ providedIn: 'root' })
export class RolesFacade {
  private readonly api = inject(Api);

  private readonly selectedSignal = signal<Role | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private etag: string | null = null;

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** `nx-data-grid`'s `fetch` callback. `page` is 0-based; the API's `page` query param is 1-based. */
  list(page: number, pageSize: number, sort: NxSortModel | null): Observable<Page<RoleListItem>> {
    return from(
      this.api.invoke(listRoles, {
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
      const response = await this.api.invoke$Response(getRole, { id });
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

  async create(request: CreateRoleRequest): Promise<Role> {
    const response = await this.api.invoke$Response(createRole, { body: request });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  async update(id: string, request: UpdateRoleRequest): Promise<Role> {
    const response = await this.api.invoke$Response(updateRole, {
      id,
      body: request,
      'If-Match': this.etag ?? undefined,
    });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  async deactivate(id: string): Promise<Role> {
    const response = await this.api.invoke$Response(deactivateRole, {
      id,
      'If-Match': this.etag ?? undefined,
    });
    this.selectedSignal.set(response.body);
    this.etag = response.headers.get('ETag');
    return response.body;
  }

  /**
   * Fetches the full permission catalog once, in a single page. The ficha
   * documents this as "unas pocas docenas de filas" (a few dozen rows), so
   * one `pageSize: 200` page (the contract's documented max) is realistic
   * for building the `resourceType` x `action` permission matrix without
   * paginating through it.
   */
  async listAllPermissions(): Promise<Permission[]> {
    const response = await this.api.invoke(listPermissions, { page: 1, pageSize: 200 });
    return [...(response.items ?? [])];
  }

  /**
   * Permission grants held by `roleId`, as of `asOf` (ISO-8601 UTC), or now
   * when `asOf` is omitted.
   */
  async getPermissions(roleId: string, asOf?: string): Promise<PermissionGrant[]> {
    const response = await this.api.invoke(getRolePermissions, { id: roleId, asOf, page: 1, pageSize: 200 });
    return [...(response.items ?? [])];
  }

  /**
   * Full-replace of `roleId`'s permission grants (`ACC-R7`): the caller
   * always sends the complete desired list of permission codes, the server
   * computes which grants to open/close.
   */
  async savePermissions(roleId: string, codes: string[]): Promise<PermissionGrant[]> {
    return this.api.invoke(updateRolePermissions, { id: roleId, body: { permissionCodes: codes } });
  }

  /** Clears `selected`/`error`/the stored `ETag` — call when entering the detail page fresh. */
  reset(): void {
    this.selectedSignal.set(null);
    this.errorSignal.set(null);
    this.etag = null;
  }
}
