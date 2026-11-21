import { RoleAssignmentListItem } from '../../../api/platform/models/role-assignment-list-item';
import { OrgTreeNode } from '../../org/tree/org-tree.model';

/**
 * How a node of the org tree relates to what the user can see:
 * - `direct`: the user holds an assignment on exactly this node (or a `GLOBAL` one);
 * - `inherited`: visible only because an assignment higher up the tree covers it;
 * - `context`: not visible itself, shown because something below it is (a group or entity on the way down);
 * - `none`: not visible.
 */
export type ScopeNodeState = 'direct' | 'inherited' | 'context' | 'none';

/** Ids the user holds an assignment on, plus whether one of them is `GLOBAL`. */
export interface DirectScope {
  readonly global: boolean;
  readonly ids: ReadonlySet<string>;
}

/** Direct targets of the (currently valid) assignments; `PROJECT` ones are not tree nodes and are skipped. */
export function directScopeOf(assignments: readonly Pick<RoleAssignmentListItem, 'scopeType' | 'scopeEntityId'>[]): DirectScope {
  let global = false;
  const ids = new Set<string>();
  for (const assignment of assignments) {
    if (assignment.scopeType === 'GLOBAL') {
      global = true;
    } else if (assignment.scopeType !== 'PROJECT' && assignment.scopeEntityId) {
      ids.add(assignment.scopeEntityId);
    }
  }
  return { global, ids };
}

/**
 * State of `node` given the ids the effective scope returned (`visibleIds`, already expanded by the
 * server) and the direct assignments. A `GROUP` is not a visibility dimension of the effective scope
 * (only entities, units, warehouses and projects are), so it is `direct` when assigned or global and
 * otherwise `context` when a descendant is visible.
 */
export function scopeStateOf(
  node: OrgTreeNode,
  visibleIds: ReadonlySet<string>,
  direct: DirectScope,
): ScopeNodeState {
  const hasVisibleDescendant = (n: OrgTreeNode): boolean =>
    n.children.some((child) => visibleIds.has(child.id) || hasVisibleDescendant(child));

  if (node.kind === 'group') {
    if (direct.global || direct.ids.has(node.id)) {
      return 'direct';
    }
    return hasVisibleDescendant(node) ? 'context' : 'none';
  }
  if (visibleIds.has(node.id)) {
    return direct.global || direct.ids.has(node.id) ? 'direct' : 'inherited';
  }
  return hasVisibleDescendant(node) ? 'context' : 'none';
}
