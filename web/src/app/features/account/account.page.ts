import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  NxDetailSkeletonComponent,
  NxEmptyStateComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxPageComponent,
  NxPageHeaderComponent,
  NxSpanDirective,
} from '../../shared/ui/layout';
import { AccountFacade, type AccountPermission } from './account.facade';

/** Display order of the actions of one area. */
const ACTION_ORDER = ['READ', 'CREATE', 'UPDATE', 'DELETE', 'APPROVE', 'EXPORT'];

/**
 * «My account» page (detail template without tabs, `conventions-frontend.md §4.5`). Reached from
 * the shell's user menu. Reads `GET /v1/me` through `AccountFacade` only (`FE-R1`) and shows names,
 * never ids: «Personal details» (name, email, business unit — read-only, locked look), «Language»
 * (same action as the top-bar selector) and «Permissions» (one row per area with its actions).
 */
@Component({
  selector: 'nx-account-page',
  imports: [
    TranslocoPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    NxDetailSkeletonComponent,
    NxEmptyStateComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxSpanDirective,
  ],
  template: `
    <nx-page width="form">
      <nx-page-header
        [breadcrumb]="true"
        [title]="'account.title' | transloco"
        [subtitle]="'account.subtitle' | transloco"
      />

      @if (facade.me(); as me) {
        @if (!me.provisioned) {
          <div class="nx-alert nx-alert--warning" role="status" data-testid="account-not-provisioned">
            <mat-icon aria-hidden="true">warning</mat-icon>
            <span class="nx-alert__text">{{ 'account.notProvisioned' | transloco }}</span>
          </div>
        }

        <nx-form-section
          class="nx-readonly"
          [title]="'account.sections.personal' | transloco"
          [hint]="'account.sections.personalHint' | transloco"
        >
          <nx-form-grid>
            <mat-form-field nxSpan="l" subscriptSizing="dynamic">
              <mat-label>{{ 'account.fields.displayName' | transloco }}</mat-label>
              <input matInput disabled [value]="me.displayName" data-testid="account-name" />
            </mat-form-field>
            <mat-form-field nxSpan="l" subscriptSizing="dynamic">
              <mat-label>{{ 'account.fields.email' | transloco }}</mat-label>
              <input matInput disabled [value]="me.email ?? '—'" data-testid="account-email" />
            </mat-form-field>
          </nx-form-grid>
          <nx-form-grid>
            <mat-form-field nxSpan="l" subscriptSizing="dynamic">
              <mat-label>{{ 'account.fields.businessUnit' | transloco }}</mat-label>
              <input
                matInput
                disabled
                [value]="facade.businessUnitLabel() ?? '—'"
                data-testid="account-business-unit"
              />
            </mat-form-field>
          </nx-form-grid>
        </nx-form-section>

        <nx-form-section
          [title]="'account.sections.language' | transloco"
          [hint]="'account.sections.languageHint' | transloco"
        >
          <nx-form-grid>
            <mat-form-field nxSpan="m" subscriptSizing="dynamic">
              <mat-label>{{ 'account.fields.preferredLanguage' | transloco }}</mat-label>
              <mat-select
                [value]="facade.language()"
                (selectionChange)="facade.setLanguage($event.value)"
                data-testid="account-language-select"
              >
                @for (option of facade.languageOptions; track option.code) {
                  <mat-option [value]="option.code" [attr.lang]="option.code">{{ option.label }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </nx-form-grid>
        </nx-form-section>

        <nx-form-section
          [title]="'account.sections.permissions' | transloco"
          [hint]="'account.sections.permissionsHint' | transloco"
        >
          @if (facade.permissionGroups().length === 0) {
            <p class="nx-empty-state" data-testid="account-no-permissions">
              <mat-icon aria-hidden="true">lock</mat-icon>
              {{ 'account.fields.noPermissions' | transloco }}
            </p>
          } @else {
            <div class="nx-acc-perms">
              @for (group of facade.permissionGroups(); track group.resource) {
                <section class="nx-acc-perms__row" [attr.data-testid]="'account-permissions-' + group.resource">
                  <h3 class="nx-acc-perms__area">{{ group.resourceLabel }}</h3>
                  <ul class="nx-acc-perms__actions">
                    @for (permission of ordered(group.permissions); track permission.code) {
                      <li class="nx-chip-outline">{{ actionLabel(permission) }}</li>
                    }
                  </ul>
                </section>
              }
            </div>
          }
        </nx-form-section>
      } @else if (facade.error()) {
        <nx-empty-state kind="error" class="nx-card" data-testid="account-load-error">
          <button mat-stroked-button type="button" data-testid="account-retry-button" (click)="facade.load()">
            <mat-icon aria-hidden="true">refresh</mat-icon>
            {{ 'common.retry' | transloco }}
          </button>
        </nx-empty-state>
      } @else {
        <nx-detail-skeleton />
      }
    </nx-page>
  `,
  styles: `
    /* Permissions: one row per area, its actions as chips; stacked below 600 px. */
    .nx-acc-perms {
      display: flex;
      flex-direction: column;
    }

    .nx-acc-perms__row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
      align-items: center;
      gap: var(--nx-space-2) var(--nx-space-4);
      padding: var(--nx-space-3) 0;
      border-bottom: 1px solid var(--nx-color-border);
    }

    .nx-acc-perms__row:first-child {
      padding-top: 0;
    }

    .nx-acc-perms__row:last-child {
      padding-bottom: 0;
      border-bottom: 0;
    }

    .nx-acc-perms__area {
      margin: 0;
      font: var(--nx-text-body);
      font-weight: 500;
      color: var(--nx-color-text);
    }

    .nx-acc-perms__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--nx-space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    @media (max-width: 599.98px) {
      .nx-acc-perms__row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
  host: { class: 'nx-form-stack' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountPage implements OnInit {
  protected readonly facade = inject(AccountFacade);
  private readonly transloco = inject(TranslocoService);

  ngOnInit(): void {
    void this.facade.load();
  }

  /** Actions in a fixed, readable order: view first, then create, change, … (unknown ones last). */
  protected ordered(permissions: readonly AccountPermission[]): AccountPermission[] {
    const rank = (p: AccountPermission) => {
      const index = ACTION_ORDER.indexOf(p.code.split(':')[1] ?? '');
      return index < 0 ? ACTION_ORDER.length : index;
    };
    return [...permissions].sort((a, b) => rank(a) - rank(b));
  }

  /** The action alone («View», «Create»): the area is already the row's heading. */
  protected actionLabel(permission: AccountPermission): string {
    const action = permission.code.split(':')[1] ?? '';
    const key = `account.permissions.actions.${action}`;
    const text = this.transloco.translate(key);
    return text && text !== key ? text : permission.label;
  }
}
