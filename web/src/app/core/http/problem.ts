/** One entry of `Problem.errors`. */
export interface ProblemFieldError {
  readonly field: string;
  readonly message: string;
}

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
  /**
   * 400: per-field detail, as the contract and `NexusExceptionHandler` send it (`[{field, message}]`).
   * A `field → messages` map is still accepted (see `fieldErrorsOf`).
   */
  readonly errors?: readonly ProblemFieldError[] | Readonly<Record<string, readonly string[]>>;
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

/** `field → messages` of a `Problem.errors` (array or map); field names start in lower case like the form controls. */
export function fieldErrorsOf(
  errors: Problem['errors'] | null | undefined,
): Readonly<Record<string, readonly string[]>> {
  const result: Record<string, string[]> = {};
  const add = (field: string, message: string) => {
    const key = field ? field.charAt(0).toLowerCase() + field.slice(1) : field;
    (result[key] ??= []).push(message);
  };
  if (Array.isArray(errors)) {
    for (const entry of errors as readonly ProblemFieldError[]) {
      add(entry.field, entry.message);
    }
  } else if (errors && typeof errors === 'object') {
    for (const [field, messages] of Object.entries(errors as Record<string, readonly string[]>)) {
      for (const message of messages) {
        add(field, message);
      }
    }
  }
  return result;
}

/**
 * Field errors of a 400 as the user should see them: the server messages are in English, so each
 * invalid field gets `message` (a translated generic text) and the server detail only goes to the
 * console. Accepts the real array shape and the legacy map; no errors gives `{}`.
 */
export function localizedFieldErrors(
  errors: Problem['errors'] | null | undefined,
  message: string,
): Readonly<Record<string, readonly string[]>> {
  const fields = fieldErrorsOf(errors);
  if (Object.keys(fields).length > 0) {
    console.warn('Validation errors from the server', errors);
  }
  const result: Record<string, readonly string[]> = {};
  for (const field of Object.keys(fields)) {
    result[field] = [message];
  }
  return result;
}
