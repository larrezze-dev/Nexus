import { Component, inject, OnInit } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AccountFacade } from './account.facade';

/**
 * "My account" page (ficha 0.5, track F point 4; extended by ficha 0.4 track F point 3).
 * Reached from the shell's user menu (`routerLink="/account"`) once `authGuard` lets the
 * navigation through. Reads `GET /v1/me` through `AccountFacade` only (`FE-R1`) and shows
 * every field the contract exposes, including the 0.4 `app_user` fields `userId`/`partyId`/
 * `businessUnitId` — `null` until 0.4-B resolves `oid -> app_user` (today's mock backend keeps
 * them `null`/`provisioned: false`; the contract itself does not change).
 */
@Component({
  selector: 'nx-account-page',
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'account.title' | transloco }}</h1>

    @if (facade.loading()) {
      <p>{{ 'account.loading' | transloco }}</p>
    } @else if (facade.error()) {
      <p role="alert">{{ facade.error() }}</p>
    } @else if (facade.me(); as me) {
      <dl>
        <dt>{{ 'account.fields.subject' | transloco }}</dt>
        <dd>{{ me.subject }}</dd>

        <dt>{{ 'account.fields.oid' | transloco }}</dt>
        <dd>{{ me.oid }}</dd>

        <dt>{{ 'account.fields.displayName' | transloco }}</dt>
        <dd>{{ me.displayName }}</dd>

        <dt>{{ 'account.fields.email' | transloco }}</dt>
        <dd>{{ me.email ?? '—' }}</dd>

        <dt>{{ 'account.fields.userId' | transloco }}</dt>
        <dd>{{ me.userId ?? '—' }}</dd>

        <dt>{{ 'account.fields.partyId' | transloco }}</dt>
        <dd>{{ me.partyId ?? '—' }}</dd>

        <dt>{{ 'account.fields.businessUnitId' | transloco }}</dt>
        <dd>{{ me.businessUnitId ?? '—' }}</dd>

        <dt>{{ 'account.fields.preferredLanguage' | transloco }}</dt>
        <dd>{{ me.preferredLanguage }}</dd>

        <dt>{{ 'account.fields.provisioned' | transloco }}</dt>
        <dd>{{ me.provisioned }}</dd>

        <dt>{{ 'account.fields.permissions' | transloco }}</dt>
        <dd>
          @if (me.permissions.length === 0) {
            {{ 'account.fields.noPermissions' | transloco }}
          } @else {
            {{ me.permissions.join(', ') }}
          }
        </dd>
      </dl>
    }
  `,
})
export class AccountPage implements OnInit {
  protected readonly facade = inject(AccountFacade);

  ngOnInit(): void {
    void this.facade.load();
  }
}
