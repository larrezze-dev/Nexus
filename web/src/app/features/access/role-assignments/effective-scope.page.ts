import { NestedTreeControl } from '@angular/cdk/tree';
import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTreeModule, MatTreeNestedDataSource } from '@angular/material/tree';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { OrgTreeNode, OrgTreeNodeKind, orgTreeNodeLabel } from '../../org/tree/org-tree.model';
import { ScopeNodeState } from './effective-scope.model';
import { EffectiveScopeFacade } from './effective-scope.facade';

const ICON_BY_KIND: Readonly<Record<OrgTreeNodeKind, string>> = {
  group: 'corporate_fare',
  legalEntity: 'apartment',
  businessUnit: 'business_center',
  warehouse: 'warehouse',
};

/** Marks shown in the legend, in this order. */
export const SCOPE_STATES: readonly ScopeNodeState[] = ['direct', 'inherited', 'context', 'none'];

/**
 * "¿Qué ve este usuario?" (`GET /v1/users/{id}/effective-scope`, `domain/access.md §4`).
 *
 * `EffectiveScopeFacade` loads the effective scope, the org tree, the user's name and their valid
 * assignments; this page marks each tree node as direct / inherited / context / none (legend on top)
 * and lists `projectIds` apart (projects are not org-tree nodes; empty before step 2.1).
 * The breadcrumb carries the user's name; states: loading, error with retry, empty.
 */
@Component({
  selector: 'nx-effective-scope-page',
  standalone: true,
  imports: [MatTreeModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, TranslocoModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>{{ 'access.effectiveScope.title' | transloco }}</h1>
    @if (facade.userName(); as userName) {
      <p data-testid="effective-scope-user">{{ userName }}</p>
    }

    <a mat-button [routerLink]="['/users', userId]">{{ 'access.effectiveScope.back' | transloco }}</a>

    @if (facade.loading()) {
      <mat-spinner diameter="32" />
      <p>{{ 'access.effectiveScope.loading' | transloco }}</p>
    } @else if (facade.error()) {
      <p role="alert" data-testid="effective-scope-error">{{ 'access.effectiveScope.loadError' | transloco }}</p>
      <button mat-stroked-button type="button" data-testid="effective-scope-retry" (click)="reload()">
        {{ 'common.retry' | transloco }}
      </button>
    } @else if (facade.loaded() && facade.isEmpty()) {
      <p data-testid="effective-scope-empty">{{ 'access.effectiveScope.empty' | transloco }}</p>
    } @else if (facade.loaded()) {
      <section data-testid="effective-scope-legend" [attr.aria-label]="'access.effectiveScope.legend.title' | transloco">
        <h2>{{ 'access.effectiveScope.legend.title' | transloco }}</h2>
        <ul>
          @for (state of legendStates(); track state) {
            <li [attr.data-testid]="'effective-scope-legend-' + state">
              <mat-icon aria-hidden="true">{{ iconForState(state) }}</mat-icon>
              <strong>{{ 'access.effectiveScope.state.' + state | transloco }}</strong>
              — {{ 'access.effectiveScope.legend.' + state | transloco }}
            </li>
          }
        </ul>
      </section>

      <mat-tree [dataSource]="dataSource" [treeControl]="treeControl" class="nx-effective-scope-tree">
        <mat-nested-tree-node *matTreeNodeDef="let node">
          <div
            class="nx-effective-scope-tree__node"
            [attr.data-state]="stateOf(node)"
            [class.nx-effective-scope-tree__node--visible]="stateOf(node) === 'direct' || stateOf(node) === 'inherited'"
          >
            <button
              mat-icon-button
              matTreeNodeToggle
              type="button"
              [attr.aria-label]="labelOf(node)"
              [style.visibility]="hasChild(0, node) ? 'visible' : 'hidden'"
            >
              <mat-icon>{{ treeControl.isExpanded(node) ? 'expand_more' : 'chevron_right' }}</mat-icon>
            </button>
            <mat-icon [attr.aria-hidden]="true">{{ iconFor(node) }}</mat-icon>
            <span class="nx-effective-scope-tree__label">{{ labelOf(node) }}</span>
            @if (stateOf(node) !== 'none') {
              <mat-icon
                class="nx-effective-scope-tree__check"
                [attr.aria-label]="'access.effectiveScope.state.' + stateOf(node) | transloco"
              >
                {{ iconForState(stateOf(node)) }}
              </mat-icon>
            }
          </div>
          <div [style.display]="treeControl.isExpanded(node) ? 'block' : 'none'" class="nx-effective-scope-tree__children">
            <ng-container matTreeNodeOutlet />
          </div>
        </mat-nested-tree-node>
      </mat-tree>

      <h2>{{ 'access.effectiveScope.projects' | transloco }}</h2>
      @if (facade.projectIds().length === 0) {
        <p data-testid="effective-scope-projects-empty">{{ 'access.effectiveScope.projectsEmpty' | transloco }}</p>
      } @else {
        <ul data-testid="effective-scope-projects-list">
          @for (projectId of facade.projectIds(); track projectId) {
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
  protected readonly facade = inject(EffectiveScopeFacade);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly translation = toSignal(this.transloco.selectTranslation());

  protected readonly treeControl = new NestedTreeControl<OrgTreeNode>((node) => node.children);
  protected readonly dataSource = new MatTreeNestedDataSource<OrgTreeNode>();
  protected userId = '';

  /** Without the assignments the direct / inherited distinction is unknown, so the legend leaves it out. */
  protected readonly legendStates = computed<readonly ScopeNodeState[]>(() =>
    this.facade.directKnown() ? SCOPE_STATES : ['direct', 'context', 'none'],
  );

  constructor() {
    effect(() => {
      // Tree data follows the facade; every group/entity starts expanded.
      const nodes = this.facade.nodes();
      this.dataSource.data = nodes;
      for (const node of this.flatten(nodes)) {
        this.treeControl.expand(node);
      }
    });

    effect(() => {
      // Breadcrumb: «<user name> · What this user sees», in the active language.
      this.translation();
      this.i18n.activeLang();
      if (this.facade.error()) {
        this.breadcrumb.setLoadFailed();
        return;
      }
      const name = this.facade.userName();
      const base = this.transloco.translate('breadcrumb.effectiveScope');
      if (this.facade.loaded()) {
        this.breadcrumb.setCurrentLabel(name ? `${name} · ${base}` : base);
      }
    });
  }

  /** Node name in the active language; reads the language signal, so it follows a switch. */
  protected labelOf(node: OrgTreeNode): string {
    return orgTreeNodeLabel(node, this.i18n.activeLang());
  }

  protected stateOf(node: OrgTreeNode): ScopeNodeState {
    return this.facade.stateOf(node);
  }

  protected readonly hasChild = (_: number, node: OrgTreeNode): boolean => node.children.length > 0;

  ngOnInit(): void {
    this.facade.reset();
    this.userId = this.route.snapshot.paramMap.get('id') ?? '';
    void this.reload();
  }

  protected async reload(): Promise<void> {
    this.breadcrumb.clearLoadFailed();
    if (!this.userId) {
      return;
    }
    await this.facade.load(this.userId);
  }

  protected iconFor(node: OrgTreeNode): string {
    return ICON_BY_KIND[node.kind];
  }

  protected iconForState(state: ScopeNodeState): string {
    switch (state) {
      case 'direct':
        return 'verified_user';
      case 'inherited':
        return 'subdirectory_arrow_right';
      case 'context':
        return 'account_tree';
      default:
        return 'visibility_off';
    }
  }

  private flatten(nodes: readonly OrgTreeNode[]): OrgTreeNode[] {
    return nodes.flatMap((node) => [node, ...this.flatten(node.children)]);
  }
}
