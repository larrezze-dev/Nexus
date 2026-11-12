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
import { NxI18nService } from '../i18n/nx-i18n.service';
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

function setup(expanded: boolean): ComponentFixture<ShellComponent> {
  TestBed.configureTestingModule({
    imports: [ShellComponent],
    providers: [
      provideRouter([
        { path: 'legal-entities', component: DummyPage },
        { path: 'warehouses', component: DummyPage },
        { path: 'roles', component: DummyPage },
      ]),
      provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
      { provide: NavigationBuilderService, useValue: { buildGrouped: () => fakeGroups } },
      { provide: BreakpointObserver, useValue: { observe: () => of({ matches: expanded, breakpoints: {} }) } },
      { provide: OidcSecurityService, useValue: { userData$: of({ userData: { name: 'Ana Pérez' } }), logoff: () => of(null) } },
      { provide: NxI18nService, useValue: { activeLang: signal('de'), setLanguage: vi.fn() } },
    ],
  });
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
  afterEach(() => localStorage.removeItem(SIDENAV_STORAGE_KEY));

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

    it('does not render an approvals bell nor the breadcrumbs (pages render them in nx-page-header)', () => {
      const fixture = setup(true);
      expect(el(fixture).textContent).not.toContain('notifications');
      expect(el(fixture).querySelector('app-breadcrumb')).toBeNull();
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
