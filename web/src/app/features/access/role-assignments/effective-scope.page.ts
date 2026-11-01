import { NestedTreeControl } from '@angular/cdk/tree';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { TranslocoModule } from '@jsverse/transloco';
import { OrgTreeFacade } from '../../org/tree/org-tree.facade';
import { OrgTreeNode, OrgTreeNodeKind } from '../../org/tree/org-tree.model';
import { RoleAssignmentsFacade } from './role-assignments.facade';

const ICON_BY_KIND: Readonly<Record<OrgTreeNodeKind, string>> = {
  group: 'corporate_fare',
  legalEntity: 'apartment',
  businessUnit: 'business_center',
  warehouse: 'warehouse',
};

/**
 * "¿Qué ve este usuario?" (`GET /v1/users/{id}/effective-scope`, `EffectiveScope`,
 * `domain/access.md §4`).
 *
 * Reuses `OrgTreeFacade`/`OrgTreeNode` (built for `features/org/tree`'s read-only
 * Group -> LegalEntity -> BusinessUnit -> Warehouse tree) instead of a second tree renderer:
 * `EffectiveScope` only returns flat id lists per dimension (`legalEntityIds`/
 * `businessUnitIds`/`warehouseIds`), with no nested shape of its own, so this page loads the
 * full org tree once and marks each `LegalEntity`/`BusinessUnit`/`Warehouse` node as visible or
 * not by checking its id against those lists. A `Group` node is marked visible when at least
 * one descendant is (it is not itself a grantable scope type, `ACC-R1`, but showing it greyed
 * out while every child is visible would be confusing).
 *
 * `projectIds` has no tree to fit into (projects don't exist as an org-tree concept, and don't
 * exist at all before step 2.1): rendered as a separate flat list below the tree, with an
 * empty-state message rather than an error when it's empty, which is simply the universal
 * case before step 2.1.
 */
@Component({
  selector: 'nx-effective-scope-page',
  standalone: true,
  imports: [MatTreeModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, TranslocoModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ 'access.effectiveScope.title' | transloco }}</h1>

    <a mat-button [routerLink]="['/users', userId()]">{{ 'access.effectiveScope.back' | transloco }}</a>

    @if (loading()) {
      <mat-spinner diameter="32" />
      <p>{{ 'access.effectiveScope.loading' | transloco }}</p>
    } @else if (error()) {
      <p role="alert">{{ error() }}</p>
    } @else if (isEmpty()) {
      <p data-testid="effective-scope-empty">{{ 'access.effectiveScope.empty' | transloco }}</p>
    } @else {
      <mat-tree [dataSource]="dataSource" [treeControl]="treeControl" class="nx-effective-scope-tree">
        <mat-nested-tree-node *matTreeNodeDef="let node">
          <div
            class="nx-effective-scope-tree__node"
            [class.nx-effective-scope-tree__node--visible]="isVisible(node)"
          >
            <button
              mat-icon-button
              matTreeNodeToggle
              type="button"
              [attr.aria-label]="node.label"
              [style.visibility]="hasChild(0, node) ? 'visible' : 'hidden'"
            >
              <mat-icon>{{ treeControl.isExpanded(node) ? 'expand_more' : 'chevron_right' }}</mat-icon>
            </button>
            <mat-icon [attr.aria-hidden]="true">{{ iconFor(node) }}</mat-icon>
            <span class="nx-effective-scope-tree__label">{{ node.label }}</span>
            @if (isVisible(node)) {
              <mat-icon class="nx-effective-scope-tree__check" [attr.aria-label]="'access.effectiveScope.visible' | transloco">
                visibility
              </mat-icon>
            }
          </div>
          <div [style.display]="treeControl.isExpanded(node) ? 'block' : 'none'" class="nx-effective-scope-tree__children">
            <ng-container matTreeNodeOutlet />
          </div>
        </mat-nested-tree-node>
      </mat-tree>

      <h2>{{ 'access.effectiveScope.projects' | transloco }}</h2>
      @if (projectIds().length === 0) {
        <p data-testid="effective-scope-projects-empty">{{ 'access.effectiveScope.projectsEmpty' | transloco }}</p>
      } @else {
        <ul data-testid="effective-scope-projects-list">
          @for (projectId of projectIds(); track projectId) {
            <li>{{ projectId }}</li>
          }
        </ul>
      }
    }
  `,
  styles: `
    .nx-effective-scope-tree__node {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      opacity: 0.5;
    }
    .nx-effective-scope-tree__node--visible {
      opacity: 1;
      font-weight: 600;
    }
    .nx-effective-scope-tree__children {
      padding-left: 2rem;
    }
  `,
})
export class EffectiveScopePage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  protected readonly orgTreeFacade = inject(OrgTreeFacade);
  private readonly assignmentsFacade = inject(RoleAssignmentsFacade);

  protected readonly treeControl = new NestedTreeControl<OrgTreeNode>((node) => node.children);
  protected readonly dataSource = new MatTreeNestedDataSource<OrgTreeNode>();

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly userId = signal('');
  protected readonly projectIds = signal<string[]>([]);

  private visibleIds = new Set<string>();

  protected readonly hasChild = (_: number, node: OrgTreeNode): boolean => node.children.length > 0;

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error.set('missing_user_id');
      this.loading.set(false);
      return;
    }
    this.userId.set(id);

    try {
      await this.orgTreeFacade.load();
      const scope = await this.assignmentsFacade.getEffectiveScope(id);
      this.visibleIds = new Set([...scope.legalEntityIds, ...scope.businessUnitIds, ...scope.warehouseIds]);
      this.projectIds.set([...scope.projectIds]);

      const nodes = this.orgTreeFacade.nodes();
      this.dataSource.data = nodes;
      for (const node of this.flatten(nodes)) {
        this.treeControl.expand(node);
      }
    } catch {
      this.error.set('load_failed');
    } finally {
      this.loading.set(false);
    }
  }

  protected iconFor(node: OrgTreeNode): string {
    return ICON_BY_KIND[node.kind];
  }

  /** True when the user has zero visible entities across every dimension
   * (`legalEntityIds`/`businessUnitIds`/`warehouseIds`/`projectIds` all empty) — shown as an
   * explicit empty-state message instead of a tree that is entirely greyed out. */
  protected isEmpty(): boolean {
    return this.visibleIds.size === 0 && this.projectIds().length === 0;
  }

  /** `GROUP` is not offered as a grant option in this repo yet — no Groups picker/facade
   * exists to back it (see the `GrantableScopeType` doc comment in
   * `features/org/users/user-detail.page.ts`) — but a `Group` node can still appear here from
   * the org tree itself, so it reads as visible when at least one descendant does, rather
   * than greying out a group whose entire content is in scope. */
  protected isVisible(node: OrgTreeNode): boolean {
    if (node.kind === 'group') {
      return node.children.some((child) => this.isVisible(child));
    }
    return this.visibleIds.has(node.id);
  }

  private flatten(nodes: readonly OrgTreeNode[]): OrgTreeNode[] {
    return nodes.flatMap((node) => [node, ...this.flatten(node.children)]);
  }
}
