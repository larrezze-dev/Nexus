import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import type { Me } from '../../api/platform/models/me';
import { NxBreadcrumbService } from '../../core/layout/breadcrumb.service';
import { AccountFacade } from './account.facade';
import { AccountPage } from './account.page';

describe('AccountPage', () => {
  const me: Me = {
    subject: 'sub-1',
    oid: 'oid-1',
    displayName: 'Jane Doe',
    email: 'jane@example.com',
    userId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c80',
    partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c81',
    businessUnitId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c82',
    preferredLanguage: 'es',
    provisioned: true,
    permissions: [],
  };

  function render(facade: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [AccountPage],
      providers: [
        provideNoopAnimations(),
        ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        { provide: AccountFacade, useValue: facade },
        {
          provide: NxBreadcrumbService,
          useValue: { claimForPage: () => () => undefined, trail: signal([]) },
        },
      ],
    });
    TestBed.inject(TranslocoService).setTranslation({ account: { permissions: { actions: { READ: 'Ver' } } } }, 'en');
    const fixture = TestBed.createComponent(AccountPage);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const baseFacade = () => ({
    load: vi.fn(),
    me: signal<Me | null>(me),
    loading: signal(false),
    error: signal<string | null>(null),
    language: signal('es'),
    languageOptions: [{ code: 'es', label: 'Español' }],
    setLanguage: vi.fn(),
    businessUnitLabel: signal<string | null>('Montaje'),
    permissionGroups: signal([
      {
        resource: 'ROLE',
        resourceLabel: 'Roles',
        permissions: [{ code: 'ROLE:READ', label: 'Roles — Ver' }],
      },
    ]),
  });

  it('shows names, never ids or true/false, and the permissions grouped by resource', () => {
    const facade = baseFacade();
    const root = render(facade);
    const value = (id: string) => root.querySelector<HTMLInputElement>(`[data-testid="${id}"]`)?.value;
    const text = [root.textContent, value('account-name'), value('account-email'), value('account-business-unit')].join(' ');

    expect(facade.load).toHaveBeenCalledOnce();
    expect(value('account-name')).toBe('Jane Doe');
    expect(value('account-email')).toBe('jane@example.com');
    expect(value('account-business-unit')).toBe('Montaje');
    // Read-only personal details: locked look; the area is the heading, the actions are chips.
    expect(root.querySelector<HTMLInputElement>('[data-testid="account-name"]')?.disabled).toBe(true);
    expect(root.querySelector('nx-form-section.nx-readonly')).not.toBeNull();
    expect(root.querySelector('[data-testid="account-permissions-ROLE"] li')?.textContent?.trim()).toBe('Ver');
    expect(root.querySelector('[data-testid="account-permissions-ROLE"] h3')?.textContent).toContain('Roles');
    expect(text).not.toContain(me.userId as string);
    expect(text).not.toContain(me.businessUnitId as string);
    expect(text).not.toContain('oid-1');
    expect(text).not.toMatch(/\b(true|false)\b/);
    expect(root.querySelector('[data-testid="account-not-provisioned"]')).toBeNull();
  });

  it('says so when the account is not provisioned, and shows «—» for a missing business unit', () => {
    const facade = baseFacade();
    facade.me.set({ ...me, provisioned: false, businessUnitId: null, email: null });
    facade.businessUnitLabel.set(null);
    const root = render(facade);

    expect(root.querySelector('[data-testid="account-not-provisioned"]')).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('[data-testid="account-business-unit"]')?.value).toBe('—');
    expect(root.querySelector<HTMLInputElement>('[data-testid="account-email"]')?.value).toBe('—');
  });

  it('shows the empty state without permissions', () => {
    const facade = baseFacade();
    facade.permissionGroups.set([]);
    expect(render(facade).querySelector('[data-testid="account-no-permissions"]')).not.toBeNull();
  });

  it('shows the load error with Retry, and the skeleton while loading', () => {
    const facade = baseFacade();
    facade.me.set(null);
    facade.error.set('Something went wrong');
    const root = render(facade);
    expect(root.querySelector('[data-testid="account-load-error"]')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('[data-testid="account-retry-button"]')?.click();
    expect(facade.load).toHaveBeenCalledTimes(2);
  });

  it('shows the skeleton before /me arrives', () => {
    const facade = baseFacade();
    facade.me.set(null);
    facade.loading.set(true);
    expect(render(facade).querySelector('nx-detail-skeleton')).not.toBeNull();
  });
});
