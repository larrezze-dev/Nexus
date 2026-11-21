import { Permission } from '../../../api/platform/models/permission';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';

export type PermissionAction = Permission['action'];

/** Column order of the matrix: reading first, then the write actions, then the rest. */
export const PERMISSION_ACTION_ORDER: readonly PermissionAction[] = [
  'READ',
  'CREATE',
  'UPDATE',
  'DELETE',
  'APPROVE',
  'EXPORT',
];

export interface PermissionMatrixColumn {
  readonly action: string;
  /** Readable action name in the active language («View», «Create»…). */
  readonly label: string;
}

export interface PermissionMatrixCell {
  readonly code: string;
  /** Readable permission name, e.g. «Audit — View». */
  readonly label: string;
  /** Server caption of the permission in the active language (tooltip), or `null`. */
  readonly caption: string | null;
}

export interface PermissionMatrixRow {
  readonly resourceType: string;
  /** Readable resource name in the active language («Audit», «Legal entities»…). */
  readonly label: string;
  readonly cells: ReadonlyMap<string, PermissionMatrixCell>;
}

export interface PermissionMatrix {
  readonly columns: readonly PermissionMatrixColumn[];
  readonly rows: readonly PermissionMatrixRow[];
}

export interface PermissionMatrixLabels {
  readonly resource: (resourceType: string) => string;
  readonly action: (action: string) => string;
  readonly language: string;
}

/**
 * Builds the `resource x action` matrix from the permission catalog: one row per resource,
 * one column per action present in the catalog (canonical order, unknown actions last), cells
 * only where a permission exists. Rows are sorted by their readable label.
 */
export function buildPermissionMatrix(
  permissions: readonly Permission[],
  labels: PermissionMatrixLabels,
): PermissionMatrix {
  const actions = new Set<string>(permissions.map((permission) => permission.action));
  const rank = (action: string): number => {
    const index = PERMISSION_ACTION_ORDER.indexOf(action as PermissionAction);
    return index < 0 ? PERMISSION_ACTION_ORDER.length : index;
  };
  const columns = [...actions]
    .sort((a, b) => rank(a) - rank(b) || a.localeCompare(b))
    .map((action) => ({ action, label: labels.action(action) }));

  const byResource = new Map<string, Map<string, PermissionMatrixCell>>();
  for (const permission of permissions) {
    const cells = byResource.get(permission.resourceType) ?? new Map<string, PermissionMatrixCell>();
    const resourceLabel = labels.resource(permission.resourceType);
    const caption = resolveCaption(permission.captionI18n, labels.language, '');
    cells.set(permission.action, {
      code: permission.code,
      label: `${resourceLabel} — ${labels.action(permission.action)}`,
      caption: caption || null,
    });
    byResource.set(permission.resourceType, cells);
  }
  const rows = [...byResource.entries()]
    .map(([resourceType, cells]) => ({ resourceType, label: labels.resource(resourceType), cells }))
    .sort((a, b) => a.label.localeCompare(b.label));
  return { columns, rows };
}

/** Readable fallback for a resource/action without a translation: `ROLE_ASSIGNMENT` -> «Role assignment». */
export function humanizeCode(code: string): string {
  const text = code.toLowerCase().replace(/_/g, ' ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

export interface PermissionDiff {
  readonly added: readonly string[];
  readonly removed: readonly string[];
}

/** Codes to open and to close going from `original` to `desired` (what the server will compute, `ACC-R7`). */
export function diffPermissions(original: ReadonlySet<string>, desired: ReadonlySet<string>): PermissionDiff {
  return {
    added: [...desired].filter((code) => !original.has(code)).sort(),
    removed: [...original].filter((code) => !desired.has(code)).sort(),
  };
}
