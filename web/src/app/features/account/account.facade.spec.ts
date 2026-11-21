import { provideHttpClient } from '@angular/common/http';
import { NX_SKIP_ERROR_NAVIGATION } from '../../core/http/skip-error-navigation';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import type { Me } from '../../api/platform/models/me';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';
import { PermissionsStore } from '../../shared/auth/permissions.store';
import { AccountFacade } from './account.facade';

const buId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c90';
const global = [{ scopeType: 'GLOBAL' as const, scopeEntityId: null }];
const me: Me = {
  subject: 'sub-1',
  oid: 'oid-1',
  displayName: 'Jane Doe',
  email: 'jane@example.com',
  userId: null,
  partyId: null,
  businessUnitId: null,
  preferredLanguage: 'fr',
  provisioned: true,
  permissions: [
    { code: 'ROLE:READ', scopes: global },
    { code: 'ROLE:UPDATE', scopes: global },
    { code: 'USER:READ', scopes: global },
    { code: 'WIDGET_THING:READ', scopes: global },
  ],
};

describe('AccountFacade', () => {
  function setup() {
    const setInitialLanguage = vi.fn();
    const setLanguage = vi.fn();
    const setPermissions = vi.fn();
    const activeLang = signal('en');

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        {
          provide: NxI18nService,
          useValue: {
            setInitialLanguage,
            setLanguage,
            activeLang,
            getActiveLanguage: () => activeLang(),
            translateErrorCode: () => 'error',
          },
        },
        { provide: PermissionsStore, useValue: { setPermissions } },
      ],
    });

    return {
      facade: TestBed.inject(AccountFacade),
      httpMock: TestBed.inject(HttpTestingController),
      setInitialLanguage,
      setLanguage,
      setPermissions,
      activeLang,
    };
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('load() stores the profile and feeds language and permissions to their hooks', async () => {
    const { facade, httpMock, setInitialLanguage, setPermissions } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush(me);
    await load;

    expect(facade.me()).toEqual(me);
    expect(setInitialLanguage).toHaveBeenCalledWith('fr');
    expect(setPermissions).toHaveBeenCalledWith(me.permissions);
    expect(facade.loading()).toBe(false);
  });

  it('resolves the business unit by its caption in the active language (never the id)', async () => {
    const { facade, httpMock, activeLang } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush({ ...me, businessUnitId: buId });
    await new Promise((resolve) => setTimeout(resolve, 0));
    httpMock
      .expectOne(`/api/platform/v1/business-units/${buId}`)
      .flush({ id: buId, code: 'BU-1', captionI18n: { de: 'Montage', en: 'Assembly' } });
    await load;

    expect(facade.businessUnitLabel()).toBe('Assembly');
    activeLang.set('de');
    expect(facade.businessUnitLabel()).toBe('Montage');
  });

  it('a business unit that cannot be read leaves the label null instead of failing the page', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush({ ...me, businessUnitId: buId });
    await new Promise((resolve) => setTimeout(resolve, 0));
    const buRequest = httpMock.expectOne(`/api/platform/v1/business-units/${buId}`);
    // A secondary name lookup must not send the user to /forbidden on a 403.
    expect(buRequest.request.context.get(NX_SKIP_ERROR_NAVIGATION)).toBe(true);
    buRequest.flush({}, { status: 500, statusText: 'err' });
    await load;

    expect(facade.businessUnitLabel()).toBeNull();
    expect(facade.error()).toBeNull();
    expect(facade.me()).not.toBeNull();
  });

  it('orders the groups (and their permissions) alphabetically by label, whatever order the server sends', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush({
      ...me,
      permissions: [
        { code: 'WIDGET:UPDATE', scopes: global },
        { code: 'USER:READ', scopes: global },
        { code: 'WIDGET:CREATE', scopes: global },
        { code: 'AUDIT:READ', scopes: global },
      ],
    });
    await load;

    const groups = facade.permissionGroups();
    expect(groups.map((g) => g.resourceLabel)).toEqual(['Audit', 'User', 'Widget']);
    expect(groups[2].permissions.map((p) => p.label)).toEqual(['Widget — Create', 'Widget — Update']);
  });

  it('groups permissions by resource with a «Resource — Action» label, humanizing what has no translation', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush(me);
    await load;

    const groups = facade.permissionGroups();
    expect(groups.map((g) => g.resource)).toEqual(['ROLE', 'USER', 'WIDGET_THING']);
    expect(groups[0].permissions.map((p) => p.code)).toEqual(['ROLE:READ', 'ROLE:UPDATE']);
    // No translation file in this test: resource and action fall back to their humanized codes.
    expect(groups[0].permissions[0].label).toBe('Role — Read');
    expect(groups[2].resourceLabel).toBe('Widget thing');
    expect(JSON.stringify(groups)).not.toMatch(/true|false/);
  });

  it('setLanguage() uses the same action as the top-bar selector, only when it changes', () => {
    const { facade, setLanguage } = setup();

    facade.setLanguage('en'); // already active
    expect(setLanguage).not.toHaveBeenCalled();
    facade.setLanguage('es');
    expect(setLanguage).toHaveBeenCalledWith('es');
  });

  it('a failed load sets the translated error and no profile', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush({ title: 'boom' }, { status: 500, statusText: 'err' });
    await load;

    expect(facade.error()).toBe('error');
    expect(facade.me()).toBeNull();
    expect(facade.loading()).toBe(false);
  });

  it('a late answer of an older load() is ignored', async () => {
    const { facade, httpMock } = setup();

    const first = facade.load();
    const second = facade.load();
    const [r1, r2] = httpMock.match('/api/platform/v1/me');
    r2.flush({ ...me, displayName: 'Newer' });
    await second;
    r1.flush({ ...me, displayName: 'Older' });
    await first;

    expect(facade.me()?.displayName).toBe('Newer');
  });
});
