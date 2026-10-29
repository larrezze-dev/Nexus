export type OrgTreeNodeKind = 'group' | 'legalEntity' | 'businessUnit' | 'warehouse';

/**
 * One node of the read-only org structure tree (Group -> LegalEntity -> BusinessUnit ->
 * Warehouse, ficha 0.4 track F point 1). Assembled client-side by `OrgTreeFacade` from four
 * separate list calls (there is no single "tree" endpoint).
 */
export interface OrgTreeNode {
  readonly id: string;
  readonly kind: OrgTreeNodeKind;
  readonly code: string;
  readonly label: string;
  /**
   * `null` for `Group` nodes: `GroupListItem` has no `isActive` field in the 0.4 contract
   * (a `Group` is not deactivated in this step) — the page renders no status badge for those.
   */
  readonly isActive: boolean | null;
  /** Not `readonly`: `NestedTreeControl`/`MatTreeNestedDataSource` (Angular Material) type
   * their `getChildren` callback as returning a mutable `T[]`. */
  readonly children: OrgTreeNode[];
}
