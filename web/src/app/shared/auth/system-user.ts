/**
 * `app_user.id` of the SYSTEM user seeded by the platform (ORG-R6: never
 * edited, never deactivated, no `external_subject`). Fixed across every
 * environment; the UI uses it only to hide actions it would reject anyway.
 */
export const SYSTEM_USER_ID = '00000000-0000-7000-8000-000000000001';

/** Thrown by the write methods for the SYSTEM user (ORG-R6) without any request being made. */
export class SystemUserReadOnlyError extends Error {
  constructor() {
    super('The SYSTEM user is read-only (ORG-R6)');
  }
}
