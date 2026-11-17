import {
  ChangeDetectionStrategy,
  Component,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  effect,
  inject,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTree, MatTreeModule } from '@angular/material/tree';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import {
  NxEmptyStateComponent,
  NxFormSectionComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { OrgTreeNode, OrgTreeNodeKind, orgTreeNodeLabel } from '../../org/tree/org-tree.model';
import { ScopeNodeState } from './effective-scope.model';
import { EffectiveScopeFacade } from './effective-scope.facade';

const ICON_BY_KIND: Readonly<Record<OrgTreeNodeKind, string>> = {
  group: 'corporate_fare',
  legalEntity: 'apartment',
  businessUnit: 'business_center',
  warehouse: 'warehouse',
};

/** Icon of each mark, in the legend and on the tree nodes (always next to its text, never alone). */
const ICON_BY_STATE: Readonly<Record<ScopeNodeState, string>> = {
  direct: 'verified_user',
  inherited: 'subdirectory_arrow_right',
  context: 'account_tree',
  none: 'visibility_off',
};

/** Marks shown in the legend, in this order. */
export const SCOPE_STATES: readonly ScopeNodeState[] = ['direct', 'inherited', 'context', 'none'];

const SKELETON_ROWS = [0, 1, 2, 3, 4];

/**
 * «What does this user see?» (`GET /v1/users/{id}/effective-scope`, `domain/access.md §4`).
 *
 * `EffectiveScopeFacade` loads the effective scope, the org tree, the user's name and their valid
 * assignments; this page marks each tree node as direct / inherited / context / none and lists
 * `projectIds` apart (projects are not org-tree nodes).
 *
 * Layout (`conventions-frontend.md §4`): full-width page like the org structure, header titled
 * with the user's name and a «Back to the user» action, then three cards — the legend, the tree
 * and the projects. Every mark is a chip with icon + text (colour is never the only cue) in the
 * `--nx-scope-*` colours. The tree reuses the org-structure tree (`mat-tree` with
 * `childrenAccessor`: ARIA tree pattern, one tab stop, arrow keys), fully expanded on load.
 * Texts inside the nodes go through `t()` so they follow a language switch.
 * States: skeleton while loading, error with retry, empty.
 */
@Component({
  selector: 'nx-effective-scope-page',
  standalone: true,
  imports: [
    MatTreeModule,
    MatIconModule,
    MatButtonModule,
    TranslocoModule,
    RouterLink,
    NxEmptyStateComponent,
    NxFormSectionComponent,
    NxPageComponent,
    NxPageHeaderComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nx-page width="full">
      <nx-page-header
        data-testid="effective-scope-user"
        [breadcrumb]="true"
        [title]="title()"
        [subtitle]="'access.effectiveScope.subtitle' | transloco"
      >
        <a nxHeaderActions mat-stroked-button data-testid="effective-scope-back" [routerLink]="['/users', userId]">
          <mat-icon aria-hidden="true">arrow_back</mat-icon>
          {{ 'access.effectiveScope.back' | transloco }}
        </a>
      </nx-page-header>

      @if (facade.error()) {
        <nx-empty-state
          kind="error"
          class="nx-card"
          data-testid="effective-scope-error"
          [message]="'access.effectiveScope.loadError' | transloco"
        >
          <button mat-stroked-button type="button" data-testid="effective-scope-retry" (click)="reload()">
            <mat-icon aria-hidden="true">refresh</mat-icon>
            {{ 'common.retry' | transloco }}
          </button>
        </nx-empty-state>
      } @else if (!facade.loading() && facade.loaded() && facade.isEmpty()) {
        <nx-empty-state
          kind="empty"
          class="nx-card"
          icon="visibility_off"
          data-testid="effective-scope-empty"
          [title]="'access.effectiveScope.empty' | transloco"
          [message]="'access.effectiveScope.emptyHint' | transloco"
        />
      } @else {
        <nx-form-section
          data-testid="effective-scope-legend"
          [title]="'access.effectiveScope.legend.title' | transloco"
        >
          <ul class="nx-scope-legend">
            @for (state of legendStates(); track state) {
              <li class="nx-scope-legend__item" [attr.data-testid]="'effective-scope-legend-' + state">
                <span class="nx-scope-chip" [class]="'nx-scope-chip--' + state">
                  <mat-icon aria-hidden="true">{{ iconForState(state) }}</mat-icon>
                  {{ t('access.effectiveScope.state.' + state) }}
                </span>
                <span class="nx-scope-legend__text">{{ t('access.effectiveScope.legend.' + state) }}</span>
              </li>
            }
          </ul>
        </nx-form-section>

        <nx-form-section [title]="'org.tree.title' | transloco">
          @if (facade.loading() || !facade.loaded()) {
            <div class="nx-scope-tree__skeleton" aria-hidden="true" data-testid="effective-scope-loading">
              @for (row of skeletonRows; track row) {
                <div class="nx-scope-tree__skeleton-row" [style.--nx-scope-tree-level]="row % 3">
                  <span class="nx-skeleton-bar" [style.width.%]="40 - (row % 3) * 6"></span>
                </div>
              }
            </div>
          } @else {
            <mat-tree
              #tree
              class="nx-scope-tree"
              [dataSource]="facade.nodes()"
              [childrenAccessor]="childrenAccessor"
              [trackBy]="trackById"
              [attr.aria-label]="'access.effectiveScope.treeLabel' | transloco"
            >
              <mat-nested-tree-node
                *matTreeNodeDef="let node"
                class="nx-scope-tree__node"
                [isExpandable]="node.children.length > 0"
                [cdkTreeNodeTypeaheadLabel]="labelOf(node)"
              >
                @let state = stateOf(node);
                <div class="nx-scope-tree__row" [attr.data-state]="state" [class.nx-scope-tree__row--none]="state === 'none'">
                  @if (node.children.length > 0) {
                    <button
                      mat-icon-button
                      matTreeNodeToggle
                      type="button"
                      tabindex="-1"
                      class="nx-scope-tree__toggle"
                      [attr.aria-label]="
                        t(tree.isExpanded(node) ? 'org.tree.collapse' : 'org.tree.expand', { name: labelOf(node) })
                      "
                    >
                      <mat-icon aria-hidden="true">{{ tree.isExpanded(node) ? 'expand_more' : 'chevron_right' }}</mat-icon>
                    </button>
                  } @else {
                    <span class="nx-scope-tree__toggle" aria-hidden="true"></span>
                  }
                  <mat-icon class="nx-scope-tree__icon" aria-hidden="true">{{ iconFor(node) }}</mat-icon>
                  <span class="nx-scope-tree__text">
                    <span class="nx-effective-scope-tree__label nx-scope-tree__name">{{ labelOf(node) }}</span>
                    <span class="nx-scope-tree__meta">
                      {{ t('org.tree.kind.' + node.kind) }}
                      @if (labelOf(node) !== node.code) {
                        · <span class="nx-mono">{{ node.code }}</span>
                      }
                    </span>
                  </span>
                  <span class="nx-scope-chip nx-scope-tree__chip" [class]="'nx-scope-chip--' + state">
                    <mat-icon aria-hidden="true">{{ iconForState(state) }}</mat-icon>
                    {{ t('access.effectiveScope.state.' + state) }}
                  </span>
                </div>
                <div role="group" class="nx-scope-tree__children" [hidden]="!tree.isExpanded(node)">
                  <ng-container matTreeNodeOutlet />
                </div>
              </mat-nested-tree-node>
            </mat-tree>
          }
        </nx-form-section>

        @if (facade.loaded()) {
          <nx-form-section [title]="'access.effectiveScope.projects' | transloco">
            @if (facade.projectIds().length === 0) {
              <p class="nx-scope-projects__empty" data-testid="effective-scope-projects-empty">
                {{ 'access.effectiveScope.projectsEmpty' | transloco }}
              </p>
            } @else {
              <ul class="nx-scope-projects" data-testid="effective-scope-projects-list">
                @for (projectId of facade.projectIds(); track projectId) {
                  <li class="nx-mono">{{ projectId }}</li>
                }
              </ul>
            }
          </nx-form-section>
        }
      }
    </nx-page>
  `,
  styles: `
    /* Mark chip: icon + text on the state's colours (--nx-scope-*, tokens.css). */
    .nx-scope-chip {
      display: inline-flex;
      align-items: center;
      gap: var(--nx-space-1);
      flex: none;
      height: var(--nx-space-5);
      padding: 0 var(--nx-space-2) 0 var(--nx-space-1);
      border: 1px solid transparent;
      border-radius: var(--nx-radius-sm);
      font: var(--nx-text-label);
      font-weight: 500;
      white-space: nowrap;
    }
    .nx-scope-chip .mat-icon {
      width: var(--nx-space-4);
      height: var(--nx-space-4);
      font-size: var(--nx-space-4);
    }
    .nx-scope-chip--direct {
      background: var(--nx-scope-direct-bg);
      color: var(--nx-scope-direct-fg);
    }
    .nx-scope-chip--inherited {
      background: var(--nx-scope-inherited-bg);
      color: var(--nx-scope-inherited-fg);
    }
    .nx-scope-chip--context {
      background: var(--nx-scope-context-bg);
      color: var(--nx-scope-context-fg);
    }
    .nx-scope-chip--none {
      background: transparent;
      border-color: var(--nx-color-outline);
      color: var(--nx-color-text-muted);
    }

    .nx-scope-legend {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(calc(7 * var(--nx-space-6)), 1fr));
      gap: var(--nx-space-4) var(--nx-space-5);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .nx-scope-legend__item {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--nx-space-2);
    }
    .nx-scope-legend__text {
      color: var(--nx-color-text-muted);
      font: var(--nx-text-body);
    }

    /* Tree: same rows as the org structure (features/org/tree), plus the mark on the right. */
    .nx-scope-tree {
      background: transparent;
      margin-inline: calc(-1 * var(--nx-space-3));
    }
    .nx-scope-tree__node {
      display: block;
      outline: none;
    }
    .nx-scope-tree__row {
      display: flex;
      align-items: center;
      gap: var(--nx-space-2);
      min-height: var(--nx-row-height);
      padding-inline: var(--nx-space-1) var(--nx-space-3);
      border-radius: var(--nx-radius-md);
    }
    .nx-scope-tree__node:focus-visible > .nx-scope-tree__row {
      outline: 2px solid var(--nx-color-primary);
      outline-offset: -2px;
    }
    .nx-scope-tree__row:hover {
      background: var(--nx-color-row-hover);
    }
    .nx-scope-tree__toggle {
      flex: none;
      width: calc(var(--nx-space-6) + var(--nx-space-2)); /* 40, Material icon button */
      height: calc(var(--nx-space-6) + var(--nx-space-2));
    }
    .nx-scope-tree__icon {
      flex: none;
      color: var(--nx-color-text-muted);
    }
    .nx-scope-tree__text {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      column-gap: var(--nx-space-3);
      flex: 1 1 auto;
      min-width: 0;
    }
    .nx-scope-tree__name {
      color: var(--nx-color-text);
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    /* Not visible: the name in the muted text colour (still AA), never faded with opacity. */
    .nx-scope-tree__row--none .nx-scope-tree__name {
      color: var(--nx-color-text-muted);
      font-weight: 400;
    }
    .nx-scope-tree__meta {
      color: var(--nx-color-text-muted);
      font: var(--nx-text-label);
    }
    .nx-scope-tree__children {
      padding-inline-start: var(--nx-tree-indent);
    }
    .nx-scope-tree__skeleton-row {
      display: flex;
      align-items: center;
      height: var(--nx-row-height);
      padding-inline-start: calc(var(--nx-scope-tree-level, 0) * var(--nx-tree-indent));
    }

    .nx-scope-projects__empty {
      margin: 0;
      color: var(--nx-color-text-muted);
      font: var(--nx-text-body);
    }
    .nx-scope-projects {
      margin: 0;
      padding-inline-start: var(--nx-space-5);
    }

    @media (max-width: 599.98px) {
      .nx-scope-tree__children {
        padding-inline-start: var(--nx-space-4);
      }
      /* Toggle · icon · name on the first line, the mark under the name (not squeezing it). */
      .nx-scope-tree__row {
        display: grid;
        grid-template-columns: auto auto minmax(0, 1fr);
        row-gap: var(--nx-space-1);
        padding-block: var(--nx-space-2);
      }
      .nx-scope-tree__chip {
        grid-column: 3;
        justify-self: start;
      }
    }
  `,
})
export class EffectiveScopePage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  protected readonly facade = inject(EffectiveScopeFacade);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly injector = inject(Injector);
  /** Emits once the active language's file is loaded (and on every switch): texts built in TS follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  private readonly tree = viewChild<MatTree<OrgTreeNode, OrgTreeNode>>('tree');

  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly childrenAccessor = (node: OrgTreeNode): OrgTreeNode[] => node.children;
  protected readonly trackById = (_: number, node: OrgTreeNode): string => node.id;
  protected userId = '';

  /** Without the assignments the direct / inherited distinction is unknown, so the legend leaves it out. */
  protected readonly legendStates = computed<readonly ScopeNodeState[]>(() =>
    this.facade.directKnown() ? SCOPE_STATES : ['direct', 'context', 'none'],
  );

  /** «What Ada Lovelace can see»; the generic question until the name is known. */
  protected readonly title = computed(() => {
    this.translation();
    const name = this.facade.userName();
    return name
      ? this.transloco.translate('access.effectiveScope.titleFor', { name })
      : this.transloco.translate('access.effectiveScope.title');
  });

  constructor() {
    // Fully expanded on every load (same as the org structure): `expandAll()` after the render, so
    // the tree's keyboard manager knows the deeper levels.
    effect(() => {
      const tree = this.tree();
      this.facade.nodes();
      if (tree) {
        untracked(() => afterNextRender(() => tree.expandAll(), { injector: this.injector }));
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

  /** Node name in the active language; reads the language signal, so it follows a switch. */
  protected labelOf(node: OrgTreeNode): string {
    return orgTreeNodeLabel(node, this.i18n.activeLang());
  }

  /** Translation in the active language, re-read on every switch (the tree's node views miss the pipe's refresh). */
  protected t(key: string, params?: Record<string, unknown>): string {
    this.translation();
    return this.transloco.translate(key, params);
  }

  protected stateOf(node: OrgTreeNode): ScopeNodeState {
    return this.facade.stateOf(node);
  }

  protected iconFor(node: OrgTreeNode): string {
    return ICON_BY_KIND[node.kind];
  }

  protected iconForState(state: ScopeNodeState): string {
    return ICON_BY_STATE[state];
  }
}
