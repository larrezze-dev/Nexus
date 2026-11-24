import { Injectable, signal } from '@angular/core';

/**
 * Identity of the signed-in user as `GET /v1/me` reports it. `Me` carries the Entra `oid` (the claim stored in
 * `app_user.external_subject`), not the `app_user.id`, so «is this record me?» compares `oid`s.
 * Cosmetic like `PermissionsStore`: the server enforces (`ACC-R13` refuses a self-deactivation).
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserStore {
  private readonly oidSignal = signal<string | null>(null);

  /** Entra `oid` of the current user; `null` until `/v1/me` resolves. */
  readonly oid = this.oidSignal.asReadonly();

  setOid(oid: string | null): void {
    this.oidSignal.set(oid);
  }

  /** Whether a user whose `oid` is `oid` is the current user (never true while unknown). */
  isMe(oid: string | null | undefined): boolean {
    const mine = this.oidSignal();
    return !!mine && !!oid && mine.toLowerCase() === oid.toLowerCase();
  }
}
