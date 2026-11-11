/**
 * Pure diff logic for `nx-audit-trail` (ficha 0.8, track F). No Angular, no i18n: labels are
 * resolved by the component from `{ field, subKey }`.
 */

/** One displayable row: a leaf of the (flattened) payload. */
export interface AuditDiffRow {
  /** Dot path of the leaf, e.g. `captionI18n.de`. */
  readonly path: string;
  /** Top-level field name, e.g. `captionI18n` (drives the `audit.fields.<field>` label). */
  readonly field: string;
  /** Remaining path for map sub-keys (`de`), `null` for top-level scalars. */
  readonly subKey: string | null;
  readonly before: unknown;
  readonly after: unknown;
}

export type AuditDiff =
  | { readonly kind: 'pii' }
  | { readonly kind: 'initial'; readonly rows: readonly AuditDiffRow[] }
  | { readonly kind: 'changes'; readonly rows: readonly AuditDiffRow[] };

export type AuditPayload = Readonly<Record<string, unknown>> | null | undefined;

/** Top-level fields never shown (identity of the aggregate, redundant with the URL). */
const IGNORED_FIELDS: ReadonlySet<string> = new Set(['id']);

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID_PATTERN.test(value);
}

/** `xxxxxxxx…xxxx` (first 8 + last 4 characters). */
export function shortUuid(value: string): string {
  return value.length <= 13 ? value : `${value.slice(0, 8)}…${value.slice(-4)}`;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Flattens nested plain objects into dot paths; arrays and scalars are leaves. */
export function flattenPayload(payload: AuditPayload): Map<string, unknown> {
  const result = new Map<string, unknown>();
  const walk = (value: Record<string, unknown>, prefix: string): void => {
    for (const [key, child] of Object.entries(value)) {
      if (prefix === '' && IGNORED_FIELDS.has(key)) {
        continue;
      }
      const path = prefix ? `${prefix}.${key}` : key;
      if (isPlainObject(child)) {
        // `{}` carries no leaf: treated as absent, so it never produces a "{}" row.
        walk(child, path);
      } else {
        result.set(path, child);
      }
    }
  };
  if (isPlainObject(payload)) {
    walk(payload, '');
  }
  return result;
}

function splitPath(path: string): { field: string; subKey: string | null } {
  const index = path.indexOf('.');
  return index < 0
    ? { field: path, subKey: null }
    : { field: path.slice(0, index), subKey: path.slice(index + 1) };
}

function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
}

/**
 * `payload === null` means a PII topic (EV-R5): nothing but who/when is kept. A `null`
 * `previousPayload` with a payload is the first event of the aggregate: all fields are shown as
 * initial values. Otherwise only added/removed/changed leaves are returned.
 */
export function computeAuditDiff(payload: AuditPayload, previousPayload: AuditPayload): AuditDiff {
  if (payload === null || payload === undefined) {
    return { kind: 'pii' };
  }
  const after = flattenPayload(payload);
  if (previousPayload === null || previousPayload === undefined) {
    const rows = [...after].map(([path, value]) => ({ path, ...splitPath(path), before: null, after: value }));
    return { kind: 'initial', rows };
  }
  const before = flattenPayload(previousPayload);
  const paths = [...new Set([...after.keys(), ...before.keys()])];
  const rows: AuditDiffRow[] = [];
  for (const path of paths) {
    const b = before.get(path);
    const a = after.get(path);
    if (!sameValue(b, a)) {
      rows.push({ path, ...splitPath(path), before: b ?? null, after: a ?? null });
    }
  }
  return { kind: 'changes', rows };
}
