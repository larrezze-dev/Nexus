import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { map } from 'rxjs';

interface NxUserClaims {
  readonly name?: string;
  readonly preferred_username?: string;
  readonly email?: string;
}

/** Display name and e-mail of the signed-in user, from the OIDC user-info claims. */
interface NxShellUser {
  readonly name: string | null;
  readonly email: string | null;
}

function toShellUser(claims: NxUserClaims | null | undefined): NxShellUser {
  const name = claims?.name?.trim() || claims?.preferred_username?.trim() || claims?.email?.trim() || null;
  const email = claims?.email?.trim() || null;
  return { name, email: email !== name ? email : null };
}

/**
 * Top-bar user menu (`conventions-frontend.md §4.2`): a button with the user's name (icon only
 * below 600 px) opening «My account» and «Sign out». Signing out is an OIDC logout through
 * `angular-auth-oidc-client` (ADR-0006), which ends the identity-provider session too.
 */
@Component({
  selector: 'nx-shell-user-menu',
  imports: [MatButtonModule, MatDividerModule, MatIconModule, MatMenuModule, RouterLink, TranslocoPipe],
  template: `
    <button
      mat-button
      type="button"
      class="nx-user-menu__trigger"
      [matMenuTriggerFor]="userMenu"
      [attr.aria-label]="
        user().name
          ? ('shell.userMenu.ariaLabelNamed' | transloco: { name: user().name })
          : ('shell.userMenu.ariaLabel' | transloco)
      "
      data-testid="nx-shell-user-menu-trigger"
    >
      <mat-icon aria-hidden="true">account_circle</mat-icon>
      @if (user().name; as name) {
        <span class="nx-user-menu__name" data-testid="nx-shell-user-name">{{ name }}</span>
      }
      <mat-icon class="nx-user-menu__caret" iconPositionEnd aria-hidden="true">arrow_drop_down</mat-icon>
    </button>
    <mat-menu #userMenu="matMenu" xPosition="before">
      @if (user().name; as name) {
        <div class="nx-user-menu__header" data-testid="nx-shell-user-menu-header">
          <div class="nx-user-menu__header-name">{{ name }}</div>
          @if (user().email; as email) {
            <div class="nx-user-menu__header-email">{{ email }}</div>
          }
        </div>
        <mat-divider />
      }
      <a mat-menu-item routerLink="/account" data-testid="nx-shell-account-link">
        <mat-icon aria-hidden="true">person</mat-icon>
        <span>{{ 'nav.account' | transloco }}</span>
      </a>
      <button mat-menu-item type="button" (click)="logout()" data-testid="nx-shell-logout">
        <mat-icon aria-hidden="true">logout</mat-icon>
        <span>{{ 'shell.userMenu.logout' | transloco }}</span>
      </button>
    </mat-menu>
  `,
  styles: `
    .nx-user-menu__trigger {
      --mat-button-text-label-text-color: var(--nx-color-text);
      --mat-button-text-horizontal-padding: var(--nx-space-3);
      --mat-button-text-icon-spacing: var(--nx-space-2);
      --mat-button-text-icon-offset: calc(-1 * var(--nx-space-1));

      max-width: 240px;
    }

    .nx-user-menu__name {
      display: block;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nx-user-menu__header {
      padding: var(--nx-space-2) var(--nx-space-4) var(--nx-space-3);
      max-width: 280px;
    }

    .nx-user-menu__header-name {
      font-weight: 500;
      overflow-wrap: anywhere;
    }

    .nx-user-menu__header-email {
      font: var(--nx-text-label);
      color: var(--nx-color-text-muted);
      overflow-wrap: anywhere;
    }

    /* Compact: icon only; the name stays in the aria-label and in the menu header. */
    @media (max-width: 599.98px) {
      .nx-user-menu__trigger .nx-user-menu__name,
      .nx-user-menu__trigger .nx-user-menu__caret {
        display: none;
      }

      .nx-user-menu__trigger {
        --mat-button-text-horizontal-padding: var(--nx-space-3);
        --mat-button-text-icon-spacing: 0;
        --mat-button-text-icon-offset: 0;
        min-width: 0;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenuComponent {
  private readonly oidc = inject(OidcSecurityService);

  private readonly claims = toSignal(this.oidc.userData$.pipe(map((result) => result?.userData as NxUserClaims | null)), {
    initialValue: null,
  });

  protected readonly user = computed(() => toShellUser(this.claims()));

  protected logout(): void {
    this.oidc.logoff().subscribe();
  }
}
