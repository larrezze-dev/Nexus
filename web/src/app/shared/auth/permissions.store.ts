import { Injectable, signal } from '@angular/core';

/**
 * Holds the permissions of the current user, as consulted by `*nxCan`
 * (`can.directive.ts`).
 *
 * This is cosmetic only (`conventions-frontend.md §6`): hiding something
 * here never substitutes for the server enforcing (or rejecting) the
 * underlying request.
 *
 * Step 0.5: `GET /v1/me` always answers `permissions: []` (see
 * `contracts/openapi/platform.yaml`, `Me` schema), so there is nothing real
 * to check yet. `has()` special-cases an empty list to `true` so every
 * `*nxCan` lets its content through, matching the step card verbatim:
 * "en 0.5 la lista está vacía y la directiva deja pasar todo mientras
 * `/me.permissions` no exista". Step 0.6 hardens this once the backend
 * actually returns permissions.
 *
 * Wiring the real `/v1/me` response into `setPermissions()` is explicitly
 * OUT of this subtask's scope: it is done by the F5 integration subtask,
 * once the `ng-openapi-gen`-generated `platform` client exists in this
 * worktree (it does not yet, in 0.5 track F / core/auth + shared/auth).
 */
@Injectable({ providedIn: 'root' })
export class PermissionsStore {
  private readonly permissionsSignal = signal<string[]>([]);

  /** Current permission list. Empty until F5 calls `setPermissions()`. */
  readonly permissions = this.permissionsSignal.asReadonly();

  /** Called by the `/v1/me` integration (F5) once that client exists. */
  setPermissions(permissions: string[]): void {
    this.permissionsSignal.set(permissions);
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
