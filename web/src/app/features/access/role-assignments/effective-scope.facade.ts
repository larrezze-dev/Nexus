import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '../../../api/platform/api';
import { namesContext } from '../../../core/http/skip-error-navigation';
import { getUserEffectiveScope } from '../../../api/platform/fn/role-assignments/get-user-effective-scope';
import { listRoleAssignments } from '../../../api/platform/fn/role-assignments/list-role-assignments';
import { getUser } from '../../../api/platform/fn/users/get-user';
import { OrgTreeFacade } from '../../org/tree/org-tree.facade';
import { OrgTreeNode } from '../../org/tree/org-tree.model';
import { DirectScope, ScopeNodeState, directScopeOf, scopeStateOf } from './effective-scope.model';

/**
 * Data of «What does this user see?» (`GET /v1/users/{id}/effective-scope`, `domain/access.md §4`):
 * the effective scope (ids per dimension), the org tree to hang them on, the user's name (for the
 * breadcrumb) and the user's currently valid assignments (to tell direct access from inherited).
 *
 * Only the latest `load()` may apply its result (`loadSeq`), so a slow response for a previous user
 * never overwrites the current one. The user's name and the assignments are display data: if either
 * cannot be read the page still shows the tree, without the name / without the direct-vs-inherited
 * distinction (`directKnown` is `false`).
 */
@Injectable({ providedIn: 'root' })
export class EffectiveScopeFacade {
  private readonly api = inject(Api);
  private readonly orgTree = inject(OrgTreeFacade);

  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly userNameSignal = signal<string | null>(null);
  private readonly nodesSignal = signal<OrgTreeNode[]>([]);
  private readonly visibleIdsSignal = signal<ReadonlySet<string>>(new Set());
  private readonly projectIdsSignal = signal<string[]>([]);
  private readonly directSignal = signal<DirectScope>({ global: false, ids: new Set() });
  private readonly directKnownSignal = signal(true);
  private readonly loadedSignal = signal(false);
  private loadSeq = 0;

  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly userName = this.userNameSignal.asReadonly();
  readonly nodes = this.nodesSignal.asReadonly();
  readonly projectIds = this.projectIdsSignal.asReadonly();
  readonly directKnown = this.directKnownSignal.asReadonly();
  /** `true` once a load finished successfully (the empty state is only meaningful then). */
  readonly loaded = this.loadedSignal.asReadonly();

  /** No visible entity in any dimension (tree dimensions and projects). */
  readonly isEmpty = computed(() => this.visibleIdsSignal().size === 0 && this.projectIdsSignal().length === 0);

  /** State of `node` for the loaded user. */
  stateOf(node: OrgTreeNode): ScopeNodeState {
    return scopeStateOf(node, this.visibleIdsSignal(), this.directSignal());
  }

  async load(userId: string): Promise<void> {
    const seq = ++this.loadSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.loadedSignal.set(false);
    try {
      const [scope, assignments, user] = await Promise.all([
        this.api.invoke(getUserEffectiveScope, { id: userId }),
        this.api.invoke(listRoleAssignments, { userId, pageSize: 200 }, namesContext()).catch(() => null),
        this.api.invoke(getUser, { id: userId }, namesContext()).catch(() => null),
        this.orgTree.load(),
      ]).then(([s, a, u]) => [s, a, u] as const);
      if (this.orgTree.error()) {
        throw new Error('org_tree_failed');
      }
      if (seq !== this.loadSeq) {
        return;
      }
      this.visibleIdsSignal.set(new Set([...scope.legalEntityIds, ...scope.businessUnitIds, ...scope.warehouseIds]));
      this.projectIdsSignal.set([...scope.projectIds]);
      this.directSignal.set(directScopeOf(assignments?.items ?? []));
      this.directKnownSignal.set(assignments !== null);
      this.userNameSignal.set(user?.fullName ?? null);
      this.nodesSignal.set(this.orgTree.nodes());
      this.loadedSignal.set(true);
    } catch {
      if (seq === this.loadSeq) {
        this.clear();
        this.errorSignal.set('load_failed');
      }
    } finally {
      if (seq === this.loadSeq) {
        this.loadingSignal.set(false);
      }
    }
  }

  /** Drops the loaded data and invalidates any load in flight; call when entering the page fresh. */
  reset(): void {
    this.loadSeq++;
    this.clear();
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }

  private clear(): void {
    this.userNameSignal.set(null);
    this.nodesSignal.set([]);
    this.visibleIdsSignal.set(new Set());
    this.projectIdsSignal.set([]);
    this.directSignal.set({ global: false, ids: new Set() });
    this.directKnownSignal.set(true);
    this.loadedSignal.set(false);
  }
}
