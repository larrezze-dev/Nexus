import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  OnInit,
  afterNextRender,
  effect,
  inject,
  untracked,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTree, MatTreeModule } from '@angular/material/tree';
import { Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import {
  NxEmptyStateComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { OrgTreeFacade } from './org-tree.facade';
import { OrgTreeNode, OrgTreeNodeKind, orgTreeNodeLabel } from './org-tree.model';

const ICON_BY_KIND: Readonly<Record<OrgTreeNodeKind, string>> = {
  group: 'corporate_fare',
  legalEntity: 'apartment',
  businessUnit: 'business_center',
  warehouse: 'warehouse',
};

/** Detail page of each kind; groups have no page of their own. */
const DETAIL_PATH_BY_KIND: Readonly<Record<OrgTreeNodeKind, string | null>> = {
  group: null,
  legalEntity: '/legal-entities',
  businessUnit: '/business-units',
  warehouse: '/warehouses',
};

const SKELETON_ROWS = [0, 1, 2, 3, 4];

/**
 * Read-only org structure: Group → Legal entity → Business unit → Warehouse, in a card under
 * the page header (breadcrumb + title), full width like a list.
 *
 * `mat-tree` (nested nodes, `childrenAccessor`) gives the ARIA tree pattern: `role="tree"`,
 * `treeitem` with `aria-level`/`aria-expanded`/`aria-setsize`, one tab stop and the keyboard
 * (↑↓ move, → expand, ← collapse, Home/End, typeahead, * expands the level). Enter or Space on
 * a node opens its detail page (`activation`); the name is also a link for the mouse. The toggle
 * and the link are out of the tab order (`tabindex="-1"`) so the tree stays one tab stop.
 *
 * Labels are resolved here, in the active language, so they follow a language switch: business
 * units and warehouses by `caption_i18n`, legal entities by trade name, groups by code. Every
 * node also says what it is («Legal entity · LE-CH»): the icon alone is not enough. Texts inside
 * the nodes go through `t()` (a signal of the loaded translation), so they follow a switch too.
 */
@Component({
  selector: 'nx-org-tree-page',
  standalone: true,
  imports: [
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    MatTreeModule,
    NxEmptyStateComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nx-page width="full">
      <nx-page-header
        [breadcrumb]="true"
        [title]="'org.tree.title' | transloco"
        [subtitle]="'org.tree.subtitle' | transloco"
      />

      <section class="nx-card nx-card--flush nx-org-tree-card">
        @if (facade.loading()) {
          <div class="nx-org-tree__skeleton" aria-hidden="true" data-testid="org-tree-loading">
            @for (row of skeletonRows; track row) {
              <div
                class="nx-org-tree__skeleton-row"
                [style.padding-inline-start.px]="16 + (row % 3) * 32"
              >
                <span
                  class="nx-data-grid__skeleton-bar"
                  [style.width.%]="40 - (row % 3) * 6"
                ></span>
              </div>
            }
          </div>
        } @else if (facade.error()) {
          <nx-empty-state kind="error" data-testid="org-tree-error">
            <button mat-stroked-button type="button" (click)="reload()">
              <mat-icon aria-hidden="true">refresh</mat-icon>
              {{ 'common.retry' | transloco }}
            </button>
          </nx-empty-state>
        } @else if (facade.nodes().length === 0) {
          <nx-empty-state
            kind="empty"
            [title]="'org.tree.empty' | transloco"
            [message]="'org.tree.emptyHint' | transloco"
            data-testid="org-tree-empty"
          />
        } @else {
          <mat-tree
            #tree
            class="nx-org-tree"
            [dataSource]="facade.nodes()"
            [childrenAccessor]="childrenAccessor"
            [trackBy]="trackById"
            [attr.aria-label]="'org.tree.treeLabel' | transloco"
          >
            <mat-nested-tree-node
              *matTreeNodeDef="let node"
              class="nx-org-tree__node"
              [isExpandable]="node.children.length > 0"
              [cdkTreeNodeTypeaheadLabel]="label(node)"
              [attr.data-testid]="'org-tree-node-' + node.id"
              (activation)="activate(node)"
            >
              <div class="nx-org-tree__row">
                @if (node.children.length > 0) {
                  <button
                    mat-icon-button
                    matTreeNodeToggle
                    type="button"
                    tabindex="-1"
                    class="nx-org-tree__toggle"
                    [attr.aria-label]="
                      t(tree.isExpanded(node) ? 'org.tree.collapse' : 'org.tree.expand', {
                        name: label(node),
                      })
                    "
                  >
                    <mat-icon aria-hidden="true">{{
                      tree.isExpanded(node) ? 'expand_more' : 'chevron_right'
                    }}</mat-icon>
                  </button>
                } @else {
                  <span class="nx-org-tree__toggle" aria-hidden="true"></span>
                }
                <mat-icon class="nx-org-tree__icon" aria-hidden="true">{{
                  iconFor(node)
                }}</mat-icon>
                <span class="nx-org-tree__text">
                  @if (detailPath(node); as path) {
                    <a class="nx-org-tree__name" tabindex="-1" [routerLink]="[path, node.id]">{{
                      label(node)
                    }}</a>
                  } @else {
                    <span class="nx-org-tree__name">{{ label(node) }}</span>
                  }
                  <span class="nx-org-tree__meta">
                    {{ t('org.tree.kind.' + node.kind) }} ·
                    <span class="nx-mono">{{ node.code }}</span>
                  </span>
                </span>
                @if (node.isActive !== null) {
                  <nx-status-badge
                    class="nx-org-tree__status"
                    [status]="node.isActive ? 'active' : 'inactive'"
                  />
                }
              </div>
              <div role="group" class="nx-org-tree__children" [hidden]="!tree.isExpanded(node)">
                <ng-container matTreeNodeOutlet />
              </div>
            </mat-nested-tree-node>
          </mat-tree>
        }
      </section>
    </nx-page>
  `,
  styles: `
    .nx-org-tree-card {
      padding-block: var(--nx-space-2);
    }
    .nx-org-tree {
      background: transparent;
    }
    .nx-org-tree__node {
      display: block;
      outline: none;
    }
    .nx-org-tree__row {
      display: flex;
      align-items: center;
      gap: var(--nx-space-2);
      min-height: 52px;
      padding-inline: var(--nx-space-2) var(--nx-space-4);
    }
    .nx-org-tree__node:focus-visible > .nx-org-tree__row {
      outline: 2px solid var(--nx-color-primary);
      outline-offset: -2px;
    }
    .nx-org-tree__row:hover {
      background: var(--nx-color-row-hover);
    }
    .nx-org-tree__toggle {
      flex: none;
      width: 40px;
      height: 40px;
    }
    .nx-org-tree__icon {
      flex: none;
      color: var(--nx-color-text-muted);
    }
    .nx-org-tree__text {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      column-gap: var(--nx-space-3);
      flex: 1 1 auto;
      min-width: 0;
    }
    .nx-org-tree__name {
      color: var(--nx-color-text);
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    a.nx-org-tree__name {
      color: var(--nx-color-primary);
      text-decoration: none;
    }
    a.nx-org-tree__name:hover {
      text-decoration: underline;
    }
    .nx-org-tree__meta {
      color: var(--nx-color-text-muted);
      font: var(--nx-text-label);
    }
    .nx-org-tree__status {
      flex: none;
    }
    .nx-org-tree__children {
      padding-inline-start: var(--nx-space-6);
    }
    .nx-org-tree__skeleton-row {
      display: flex;
      align-items: center;
      height: 52px;
    }
    @media (max-width: 599.98px) {
      .nx-org-tree__children {
        padding-inline-start: var(--nx-space-4);
      }
      .nx-org-tree__row {
        padding-inline-end: var(--nx-space-3);
      }
    }
  `,
})
export class OrgTreePage implements OnInit {
  protected readonly facade = inject(OrgTreeFacade);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly injector = inject(Injector);
  private readonly transloco = inject(TranslocoService);

  /**
   * Emits once the active language's file is loaded (and on every switch). Texts inside the tree
   * nodes are translated through it rather than the pipe: the nodes are views the tree creates,
   * and the pipe did not refresh them on a language switch.
   */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  private readonly tree = viewChild<MatTree<OrgTreeNode, OrgTreeNode>>('tree');

  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly childrenAccessor = (node: OrgTreeNode): OrgTreeNode[] => node.children;
  protected readonly trackById = (_: number, node: OrgTreeNode): string => node.id;

  constructor() {
    // Fully expanded on every load: the structure is small and the point is to see all of it.
    // `expandAll()` after the render, not `[isExpanded]` on each node: expanding nodes while the
    // tree renders them leaves its keyboard manager without the deeper levels.
    effect(() => {
      const tree = this.tree();
      this.facade.nodes();
      if (tree) {
        untracked(() => afterNextRender(() => tree.expandAll(), { injector: this.injector }));
      }
    });
  }

  ngOnInit(): void {
    void this.facade.load();
  }

  /** «Retry» of the error state. */
  protected reload(): void {
    void this.facade.load();
  }

  /** Name of a node in the active language (re-read on every language switch). */
  protected label(node: OrgTreeNode): string {
    return orgTreeNodeLabel(node, this.i18n.activeLang());
  }

  /** Translation in the active language, re-read on every language switch. */
  protected t(key: string, params?: Record<string, unknown>): string {
    this.translation();
    return this.transloco.translate(key, params);
  }

  protected iconFor(node: OrgTreeNode): string {
    return ICON_BY_KIND[node.kind];
  }

  protected detailPath(node: OrgTreeNode): string | null {
    return DETAIL_PATH_BY_KIND[node.kind];
  }

  /** Enter/Space on a node: open its detail page; a group (no page) expands or collapses. */
  protected activate(node: OrgTreeNode): void {
    const path = this.detailPath(node);
    if (path) {
      void this.router.navigate([path, node.id]);
    } else if (node.children.length > 0) {
      this.tree()?.toggle(node);
    }
  }
}
