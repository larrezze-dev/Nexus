import { Injectable, signal } from '@angular/core';
import { MePermission } from '../../api/platform/models/me-permission';

/**
 * Holds the permissions of the current user, as consulted by `*nxCan`
 * (`can.directive.ts`).
 *
 * This is cosmetic only (`conventions-frontend.md §6`): hiding something
 * here never substitutes for the server enforcing (or rejecting) the
 * underlying request.
 *
 * `GET /v1/me` answers `permissions: MePermission[]` (`code` + `scopes`,
 * see `contracts/openapi/platform.yaml`, `Me`/`MePermission` schemas).
 * `AccountFacade.load()` feeds that list straight into `setPermissions()`,
 * which keeps only each entry's `code` — this store does not need to be
 * scope-aware for `*nxCan` (a future step may add scope-aware UI; until
 * then this only checks membership by `code`). `has()` still special-cases
 * an empty list to `true` so every `*nxCan` lets its content through while
 * no permission data is loaded yet (e.g. before `/v1/me` resolves).
 */
@Injectable({ providedIn: 'root' })
export class PermissionsStore {
  private readonly permissionsSignal = signal<string[]>([]);

  /** Current permission list. Empty until `setPermissions()` is called. */
  readonly permissions = this.permissionsSignal.asReadonly();

  /** Called by `AccountFacade.load()` once `GET /v1/me` resolves. */
  setPermissions(permissions: MePermission[]): void {
    this.permissionsSignal.set(permissions.map((permission) => permission.code));
  }

  /**
   * Whether the current user may act on `permission` (`'RESOURCE:ACTION'`).
   *
   * Step 0.5 special case: while the list is empty (always true today),
   * this returns `true` unconditionally — there is no real permission data
   * to check against yet, so nothing in the UI should be hidden because of
   * it. From 0.6 onward, once the list is actually populated, this checks
   * for real membership.
   */
  has(permission: string): boolean {
    const current = this.permissionsSignal();

    if (current.length === 0) {
      return true;
    }

    return current.includes(permission);
  }
}
