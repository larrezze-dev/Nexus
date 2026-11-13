import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';

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
  /**
   * Translatable name (`caption_i18n`) of business units and warehouses; `null` for the other
   * kinds. Kept raw so the page shows it in the active language and follows a language switch.
   */
  readonly captionI18n?: I18NText | null;
  /** Plain name: a legal entity's trade name (or legal name); `null` when the kind has none (group). */
  readonly name?: string | null;
  /**
   * Label in the language that was active when the tree loaded ({@link orgTreeNodeLabel}). Kept
   * for `features/access` (effective scope); the org tree page re-resolves it on every switch.
   */
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

/** Label of a node in `language`: caption (active → de → en → first), else the name, else the code. */
export function orgTreeNodeLabel(
  node: Pick<OrgTreeNode, 'captionI18n' | 'name' | 'code'>,
  language: string,
): string {
  if (node.captionI18n) {
    return resolveCaption(node.captionI18n, language, node.code);
  }
  return node.name || node.code;
}
