import { NestedTreeControl } from '@angular/cdk/tree';
import { ChangeDetectionStrategy, Component, OnInit, effect, inject } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { TranslocoPipe } from '@jsverse/transloco';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { OrgTreeFacade } from './org-tree.facade';
import { OrgTreeNode, OrgTreeNodeKind } from './org-tree.model';

const ICON_BY_KIND: Readonly<Record<OrgTreeNodeKind, string>> = {
  group: 'corporate_fare',
  legalEntity: 'apartment',
  businessUnit: 'business_center',
  warehouse: 'warehouse',
};

/**
 * Read-only org structure tree: Group -> LegalEntity -> BusinessUnit -> Warehouse
 * (ficha 0.4 track F point 1, `conventions-frontend.md §4`).
 *
 * Uses Angular Material's `MatTree` (`MatTreeNestedDataSource` + `NestedTreeControl`) rather
 * than a hand-rolled recursive component: the tree is small, fully assembled client-side
 * (`OrgTreeFacade`, no server-side lazy loading needed) and `MatTree` already ships the
 * expand/collapse state, keyboard navigation and a11y roles this would otherwise have to be
 * reimplemented by hand.
 */
@Component({
  selector: 'nx-org-tree-page',
  imports: [MatTreeModule, MatIconModule, MatProgressSpinnerModule, TranslocoPipe, NxStatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ 'org.tree.title' | transloco }}</h1>

    @if (facade.loading()) {
      <mat-spinner diameter="32" />
    } @else if (facade.error()) {
      <p role="alert">{{ facade.error() }}</p>
    } @else if (facade.nodes().length === 0) {
      <p>{{ 'org.tree.empty' | transloco }}</p>
    }

    <mat-tree [dataSource]="dataSource" [treeControl]="treeControl" class="nx-org-tree">
      <mat-nested-tree-node *matTreeNodeDef="let node">
        <div class="nx-org-tree__node">
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
          <span class="nx-org-tree__label">{{ node.label }}</span>
          @if (node.isActive !== null) {
            <nx-status-badge [status]="node.isActive ? 'active' : 'inactive'" />
          }
        </div>
        <div [style.display]="treeControl.isExpanded(node) ? 'block' : 'none'" class="nx-org-tree__children">
          <ng-container matTreeNodeOutlet />
        </div>
      </mat-nested-tree-node>
    </mat-tree>
  `,
  styles: `
    .nx-org-tree__node {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .nx-org-tree__children {
      padding-left: 2rem;
    }
  `,
})
export class OrgTreePage implements OnInit {
  protected readonly facade = inject(OrgTreeFacade);

  protected readonly treeControl = new NestedTreeControl<OrgTreeNode>((node) => node.children);
  protected readonly dataSource = new MatTreeNestedDataSource<OrgTreeNode>();

  protected readonly hasChild = (_: number, node: OrgTreeNode): boolean => node.children.length > 0;

  constructor() {
    // `MatTreeNestedDataSource.data` is a plain property (not a signal input), so it is
    // re-assigned imperatively inside `effect()` whenever `facade.nodes()` changes.
    effect(() => {
      const nodes = this.facade.nodes();
      this.dataSource.data = nodes;
      // A read-only structure tree with at most a few dozen nodes (ficha 0.4 dev seed: 1
      // group, 2 LE, 2 BU, 2 warehouses) is more useful fully expanded by default.
      for (const node of this.flatten(nodes)) {
        this.treeControl.expand(node);
      }
    });
  }

  ngOnInit(): void {
    void this.facade.load();
  }

  protected iconFor(node: OrgTreeNode): string {
    return ICON_BY_KIND[node.kind];
  }

  private flatten(nodes: readonly OrgTreeNode[]): OrgTreeNode[] {
    return nodes.flatMap((node) => [node, ...this.flatten(node.children)]);
  }
}
