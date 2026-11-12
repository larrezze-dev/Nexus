import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LANGUAGE_STORAGE_KEY } from '../i18n/nx-i18n.service';
import { UserMenuComponent } from './user-menu.component';

describe('UserMenuComponent', () => {
  afterEach(() => localStorage.removeItem(LANGUAGE_STORAGE_KEY));

  let fixture: ComponentFixture<UserMenuComponent>;

  function setup(userData: Record<string, string> | null) {
    const logoff = vi.fn(() => of(null));
    TestBed.configureTestingModule({
      imports: [UserMenuComponent],
      providers: [
        provideRouter([]),
        provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
        { provide: OidcSecurityService, useValue: { userData$: of({ userData }), logoff } },
      ],
    });
    fixture = TestBed.createComponent(UserMenuComponent);
    fixture.detectChanges();
    return { logoff };
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="nx-shell-user-menu-trigger"]');
  }

  function open(): void {
    trigger().click();
    fixture.detectChanges();
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((node) => node.remove()));

  it("shows the user's name on the button and in the menu header with the e-mail", () => {
    setup({ name: 'Ana Pérez', email: 'ana@example.test' });
    expect(trigger().textContent).toContain('Ana Pérez');
    expect(trigger().getAttribute('aria-label')).toBe('shell.userMenu.ariaLabelNamed');
    open();
    const header = document.querySelector('[data-testid="nx-shell-user-menu-header"]');
    expect(header?.textContent).toContain('Ana Pérez');
    expect(header?.textContent).toContain('ana@example.test');
  });

  it('falls back to the user name claim, then to the e-mail', () => {
    setup({ preferred_username: 'usera', email: 'usera@example.test' });
    expect(trigger().textContent).toContain('usera');
  });

  it('renders an icon-only, still labelled button when no claims are known yet', () => {
    setup(null);
    expect(fixture.nativeElement.querySelector('[data-testid="nx-shell-user-name"]')).toBeNull();
    expect(trigger().getAttribute('aria-label')).toBe('shell.userMenu.ariaLabel');
  });

  it('offers My account and signs out through the OIDC logout', () => {
    const { logoff } = setup({ name: 'Ana Pérez' });
    open();
    expect(document.querySelector('[data-testid="nx-shell-account-link"]')?.getAttribute('href')).toBe('/account');
    (document.querySelector('[data-testid="nx-shell-logout"]') as HTMLButtonElement).click();
    expect(logoff).toHaveBeenCalledTimes(1);
  });

  it('clears the remembered language (nx.lang) when signing out', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr');
    const { logoff } = setup({ name: 'Ana Pérez' });
    open();
    (document.querySelector('[data-testid="nx-shell-logout"]') as HTMLButtonElement).click();
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
    expect(logoff).toHaveBeenCalledTimes(1);
  });
});
