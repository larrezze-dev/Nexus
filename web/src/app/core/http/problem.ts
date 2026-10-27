/**
 * RFC 9457 `Problem` body, as returned by every NEXUS service on error
 * (conventions-api.md §6, conventions-backend.md §7).
 *
 * Only the fields the frontend inspects are modeled here; services may add more.
 */
export interface Problem {
  /** e.g. `"validation"`, `"business_rule"`, `"concurrency"`, or a URI for other problem types. */
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  /** 400: field name → list of translated or translatable messages. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
  /** 422: business-rule id (`XXX-Rn`) the client can map to an `errors.<code>` i18n key. */
  readonly code?: string;
  /** 403: permission the caller was missing, e.g. `ASSET_TYPE:CREATE`. */
  readonly requiredPermission?: string;
  readonly [key: string]: unknown;
}

/** Narrows an unknown HTTP error body to a `Problem`. Every NEXUS error body is a JSON object. */
export function isProblem(body: unknown): body is Problem {
  return !!body && typeof body === 'object';
}
