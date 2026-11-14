import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { AccountFacade } from './account.facade';

/**
 * «My account» page. Reached from the shell's user menu once `authGuard` lets the navigation
 * through. Reads `GET /v1/me` through `AccountFacade` only (`FE-R1`) and shows names, never ids:
 * name, email, language (changeable here, same action as the top-bar selector), business unit and
 * the permissions grouped by resource.
 */
@Component({
  selector: 'nx-account-page',
  imports: [TranslocoPipe, MatFormFieldModule, MatSelectModule],
  template: `
    <h1>{{ 'account.title' | transloco }}</h1>

    @if (facade.loading()) {
      <p>{{ 'account.loading' | transloco }}</p>
    } @else if (facade.error()) {
      <p role="alert">{{ facade.error() }}</p>
    } @else if (facade.me(); as me) {
      @if (!me.provisioned) {
        <p role="status" data-testid="account-not-provisioned">{{ 'account.notProvisioned' | transloco }}</p>
      }
      <dl>
        <dt>{{ 'account.fields.displayName' | transloco }}</dt>
        <dd data-testid="account-name">{{ me.displayName }}</dd>

        <dt>{{ 'account.fields.email' | transloco }}</dt>
        <dd data-testid="account-email">{{ me.email ?? '—' }}</dd>

        <dt>{{ 'account.fields.businessUnit' | transloco }}</dt>
        <dd data-testid="account-business-unit">{{ facade.businessUnitLabel() ?? '—' }}</dd>
      </dl>

      <mat-form-field>
        <mat-label>{{ 'account.fields.preferredLanguage' | transloco }}</mat-label>
        <mat-select
          [value]="facade.language()"
          (selectionChange)="facade.setLanguage($event.value)"
          data-testid="account-language-select"
        >
          @for (option of facade.languageOptions; track option.code) {
            <mat-option [value]="option.code">{{ option.label }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <h2>{{ 'account.fields.permissions' | transloco }}</h2>
      @if (facade.permissionGroups().length === 0) {
        <p data-testid="account-no-permissions">{{ 'account.fields.noPermissions' | transloco }}</p>
      } @else {
        @for (group of facade.permissionGroups(); track group.resource) {
          <section [attr.data-testid]="'account-permissions-' + group.resource">
            <h3>{{ group.resourceLabel }}</h3>
            <ul>
              @for (permission of group.permissions; track permission.code) {
                <li>{{ permission.label }}</li>
              }
            </ul>
          </section>
        }
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountPage implements OnInit {
  protected readonly facade = inject(AccountFacade);

  ngOnInit(): void {
    void this.facade.load();
  }
}
