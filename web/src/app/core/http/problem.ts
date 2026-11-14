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

/** The `Problem` body of an `HttpErrorResponse`-like error, if it carries one. */
export function problemOf(error: unknown): Problem | undefined {
  const body =
    error && typeof error === 'object' && 'error' in error
      ? (error as { error?: unknown }).error
      : undefined;
  return isProblem(body) ? body : undefined;
}

function statusOf(error: unknown): number | undefined {
  return error && typeof error === 'object' && 'status' in error
    ? (error as { status?: number }).status
    : undefined;
}

/**
 * 409 `CONCURRENCY` (stale `If-Match`): `errorInterceptor` already opened the concurrency dialog,
 * so the caller shows nothing. The backend's `NexusProblemType` sends `type: .../problems/conflict`
 * with `code: "CONCURRENCY"`; the legacy `type: "concurrency"` is still accepted.
 */
export function isConcurrencyConflict(error: unknown): boolean {
  if (statusOf(error) !== 409) {
    return false;
  }
  const problem = problemOf(error);
  return problem?.code === 'CONCURRENCY' || problem?.type === 'concurrency';
}

/** 409 `CONFLICT` (duplicate key, e.g. a record with this `code` already exists). */
export function isDuplicateConflict(error: unknown): boolean {
  return statusOf(error) === 409 && problemOf(error)?.code === 'CONFLICT';
}

/**
 * Field errors of a 400 as the user should see them: the server messages are in English, so each
 * invalid field gets `message` (a translated generic text) and the server detail only goes to the
 * console for support.
 */
export function localizedFieldErrors(
  errors: Readonly<Record<string, readonly string[]>>,
  message: string,
): Readonly<Record<string, readonly string[]>> {
  console.warn('Validation errors from the server', errors);
  const result: Record<string, readonly string[]> = {};
  for (const field of Object.keys(errors)) {
    result[field] = [message];
  }
  return result;
}
