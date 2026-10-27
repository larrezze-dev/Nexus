import { Component, inject, OnInit } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AccountFacade } from './account.facade';

/**
 * "My account" page (ficha 0.5, track F point 4). Reached from the shell's user menu
 * (`routerLink="/account"`) once `authGuard` lets the navigation through. Reads `GET /v1/me`
 * through `AccountFacade` only (`FE-R1`) and shows every field the step 0.5 contract exposes.
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
