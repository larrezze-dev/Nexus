import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import type { Me } from '../../api/platform/models/me';
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
      ],
    });
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
    const text = root.textContent ?? '';

    expect(facade.load).toHaveBeenCalledOnce();
    expect(text).toContain('Jane Doe');
    expect(text).toContain('jane@example.com');
    expect(text).toContain('Montaje');
    expect(text).toContain('Roles — Ver');
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
    expect(root.querySelector('[data-testid="account-business-unit"]')?.textContent?.trim()).toBe('—');
    expect(root.querySelector('[data-testid="account-email"]')?.textContent?.trim()).toBe('—');
  });

  it('shows the empty state without permissions', () => {
    const facade = baseFacade();
    facade.permissionGroups.set([]);
    expect(render(facade).querySelector('[data-testid="account-no-permissions"]')).not.toBeNull();
  });

  it('shows the load error', () => {
    const facade = baseFacade();
    facade.me.set(null);
    facade.error.set('Something went wrong');
    expect(render(facade).querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
  });
});
