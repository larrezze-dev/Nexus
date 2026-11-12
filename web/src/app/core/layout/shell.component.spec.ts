import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSidenav } from '@angular/material/sidenav';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionsStore } from '../../shared/auth/permissions.store';
import { NxI18nService } from '../i18n/nx-i18n.service';
import { NxBreadcrumbService } from './breadcrumb.service';
import { NavigationBuilderService } from './navigation-builder.service';
import { NxNavGroup } from './nav-item.model';
import { SIDENAV_STORAGE_KEY, ShellComponent } from './shell.component';

@Component({ standalone: true, template: '<p>page</p>' })
class DummyPage {}

const fakeGroups: NxNavGroup[] = [
  {
    navGroup: 'org',
    titleKey: 'nav.groups.org',
    items: [
      { titleKey: 'nav.org.legalEntities', path: '/legal-entities', icon: 'domain', navGroup: 'org' },
      { titleKey: 'nav.warehouses', path: '/warehouses', icon: 'warehouse', navGroup: 'org' },
    ],
  },
  {
    navGroup: 'access',
    titleKey: 'nav.groups.access',
    items: [{ titleKey: 'nav.access.roles', path: '/roles', icon: 'shield', navGroup: 'access' }],
  },
];

function setup(
  expanded: boolean,
  options: { groups?: NxNavGroup[]; permissions?: string[] } = {},
): ComponentFixture<ShellComponent> {
  TestBed.configureTestingModule({
    imports: [ShellComponent],
    providers: [
      provideRouter([
        { path: 'legal-entities', component: DummyPage },
        { path: 'warehouses', component: DummyPage },
        { path: 'roles', component: DummyPage },
      ]),
      provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
      { provide: NavigationBuilderService, useValue: { buildGrouped: () => options.groups ?? fakeGroups } },
      { provide: BreakpointObserver, useValue: { observe: () => of({ matches: expanded, breakpoints: {} }) } },
      { provide: OidcSecurityService, useValue: { userData$: of({ userData: { name: 'Ana Pérez' } }), logoff: () => of(null) } },
      { provide: NxI18nService, useValue: { activeLang: signal('de'), setLanguage: vi.fn() } },
    ],
  });
  TestBed.inject(PermissionsStore).setPermissions((options.permissions ?? []).map((code) => ({ code, scopes: [] }) as never));
  const fixture = TestBed.createComponent(ShellComponent);
  fixture.detectChanges();
  return fixture;
}

function el(fixture: ComponentFixture<ShellComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function sidenav(fixture: ComponentFixture<ShellComponent>): MatSidenav {
  return fixture.debugElement.query(By.directive(MatSidenav)).componentInstance as MatSidenav;
}

function toggle(fixture: ComponentFixture<ShellComponent>): HTMLButtonElement {
  return el(fixture).querySelector('[data-testid="nx-shell-menu-toggle"]') as HTMLButtonElement;
}

describe('ShellComponent', () => {
  beforeEach(() => localStorage.removeItem(SIDENAV_STORAGE_KEY));
  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.removeItem(SIDENAV_STORAGE_KEY);
  });

  describe('top bar', () => {
    it('shows the menu button, the NEXUS brand, the language selector and the user menu — no global search', () => {
      const fixture = setup(true);
      const root = el(fixture);
      expect(toggle(fixture)).toBeTruthy();
      expect(root.querySelector('.nx-shell__brand')?.textContent).toContain('NEXUS');
      expect(root.querySelector('[data-testid="nx-shell-language-trigger"]')).toBeTruthy();
      expect(root.querySelector('[data-testid="nx-shell-user-menu-trigger"]')?.textContent).toContain('Ana Pérez');
      expect(root.querySelector('input')).toBeNull();
    });

    it('does not render an approvals bell, and renders the breadcrumb above the routed page (temporary, TODO PR-5)', () => {
      const fixture = setup(true);
      expect(el(fixture).textContent).not.toContain('notifications');
      const main = el(fixture).querySelector('main#nx-main') as HTMLElement;
      expect(main.firstElementChild?.tagName.toLowerCase()).toBe('app-breadcrumb');
    });

    it('drops its breadcrumb while the page renders it in nx-page-header, and brings it back after', () => {
      const fixture = setup(true);
      const release = TestBed.inject(NxBreadcrumbService).claimForPage();
      fixture.detectChanges();
      expect(el(fixture).querySelector('main#nx-main app-breadcrumb')).toBeNull();

      release();
      release(); // idempotent: a second call must not un-claim another page's header
      fixture.detectChanges();
      expect(el(fixture).querySelector('main#nx-main app-breadcrumb')).toBeTruthy();
    });

    it('keeps the breadcrumb hidden when the next page claims before the previous one releases', () => {
      const fixture = setup(true);
      const service = TestBed.inject(NxBreadcrumbService);
      const releasePrevious = service.claimForPage();
      const releaseNext = service.claimForPage();
      releasePrevious();
      fixture.detectChanges();
      expect(el(fixture).querySelector('main#nx-main app-breadcrumb')).toBeNull();
      releaseNext();
      fixture.detectChanges();
      expect(el(fixture).querySelector('main#nx-main app-breadcrumb')).toBeTruthy();
    });

    it('offers a skip link to the main content', () => {
      const fixture = setup(true);
      const skip = el(fixture).querySelector('[data-testid="nx-shell-skip-link"]') as HTMLAnchorElement;
      const main = el(fixture).querySelector('main#nx-main') as HTMLElement;
      expect(skip.getAttribute('href')).toBe('#nx-main');
      skip.click();
      expect(document.activeElement).toBe(main);
    });
  });

  describe('side menu', () => {
    it('renders the groups with their headings and items, inside a labelled nav', () => {
      const fixture = setup(true);
      const nav = el(fixture).querySelector('nav') as HTMLElement;
      expect(nav.getAttribute('aria-label')).toBe('shell.nav.ariaLabel');
      const groups = nav.querySelectorAll('[role="group"]');
      expect(groups.length).toBe(2);
      expect(groups[0].textContent).toContain('nav.groups.org');
      expect(groups[0].querySelectorAll('a').length).toBe(2);
      expect(groups[1].textContent).toContain('nav.groups.access');
      expect(groups[1].getAttribute('aria-labelledby')).toBe('nx-nav-group-access');
    });

    it('hides items without permission and a group left with no visible items, heading included', () => {
      const groups: NxNavGroup[] = [
        { navGroup: 'org', titleKey: 'nav.groups.org', items: [{ titleKey: 'nav.warehouses', path: '/warehouses', navGroup: 'org', permission: 'WAREHOUSE:READ' }] },
        {
          navGroup: 'access',
          titleKey: 'nav.groups.access',
          items: [{ titleKey: 'nav.access.roles', path: '/roles', navGroup: 'access', permission: 'ROLE:READ' }],
        },
      ];
      const fixture = setup(true, { groups, permissions: ['WAREHOUSE:READ'] });
      const root = el(fixture);
      expect(root.querySelectorAll('[role="group"]').length).toBe(1);
      expect(root.textContent).toContain('nav.groups.org');
      expect(root.textContent).not.toContain('nav.groups.access');
      expect(root.querySelectorAll('.nx-shell__nav a').length).toBe(1);
    });

    it('exposes a single navigation landmark (mat-nav-list does not add a nested one)', () => {
      const fixture = setup(true);
      const root = el(fixture);
      expect(root.querySelectorAll('nav, [role="navigation"]').length).toBe(1);
    });

    it('marks the entry of the current route as active', async () => {
      const fixture = setup(true);
      await TestBed.inject(Router).navigateByUrl('/warehouses');
      fixture.detectChanges();
      const active = el(fixture).querySelector('a[aria-current="page"]');
      expect(active?.textContent).toContain('nav.warehouses');
      expect(active?.classList).toContain('mdc-list-item--activated');
      expect(el(fixture).querySelectorAll('a[aria-current="page"]').length).toBe(1);
    });
  });

  describe('≥ 1280 px', () => {
    it('is beside the content and open by default', () => {
      const fixture = setup(true);
      expect(sidenav(fixture).mode).toBe('side');
      expect(sidenav(fixture).opened).toBe(true);
      expect(toggle(fixture).getAttribute('aria-expanded')).toBe('true');
    });

    it('☰ hides it entirely and remembers the choice', () => {
      const fixture = setup(true);
      toggle(fixture).click();
      fixture.detectChanges();
      expect(sidenav(fixture).opened).toBe(false);
      expect(toggle(fixture).getAttribute('aria-expanded')).toBe('false');
      expect(localStorage.getItem(SIDENAV_STORAGE_KEY)).toBe('closed');
    });

    it('starts hidden when the user hid it last time', () => {
      localStorage.setItem(SIDENAV_STORAGE_KEY, 'closed');
      const fixture = setup(true);
      expect(sidenav(fixture).opened).toBe(false);
    });

    it('still opens when reading localStorage throws', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('denied');
      });
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('denied');
      });
      const fixture = setup(true);
      expect(sidenav(fixture).opened).toBe(true);
      expect(() => toggle(fixture).click()).not.toThrow();
      fixture.detectChanges();
      expect(sidenav(fixture).opened).toBe(false);
    });

    it('stays open after navigating', async () => {
      const fixture = setup(true);
      await TestBed.inject(Router).navigateByUrl('/roles');
      fixture.detectChanges();
      expect(sidenav(fixture).opened).toBe(true);
    });
  });

  describe('< 1280 px', () => {
    it('opens over the content and is closed on entry, whatever was remembered for wide screens', () => {
      localStorage.setItem(SIDENAV_STORAGE_KEY, 'open');
      const fixture = setup(false);
      expect(sidenav(fixture).mode).toBe('over');
      expect(sidenav(fixture).opened).toBe(false);
    });

    it('☰ opens it without touching the wide-screen preference, and navigating closes it', async () => {
      const fixture = setup(false);
      toggle(fixture).click();
      fixture.detectChanges();
      expect(sidenav(fixture).opened).toBe(true);
      expect(localStorage.getItem(SIDENAV_STORAGE_KEY)).toBeNull();

      await TestBed.inject(Router).navigateByUrl('/legal-entities');
      fixture.detectChanges();
      expect(sidenav(fixture).opened).toBe(false);
    });
  });
});
