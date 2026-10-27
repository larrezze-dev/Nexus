import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Me } from '../../api/platform/models/me';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';
import { PermissionsStore } from '../../shared/auth/permissions.store';
import { AccountFacade } from './account.facade';

const me: Me = {
  subject: 'sub-1',
  oid: 'oid-1',
  displayName: 'Jane Doe',
  email: 'jane@example.com',
  preferredLanguage: 'fr',
  provisioned: false,
  permissions: ['ASSET_TYPE:READ'],
};

describe('AccountFacade', () => {
  function setup() {
    const setInitialLanguage = vi.fn();
    const setPermissions = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { setInitialLanguage, translateErrorCode: () => 'error' } },
        { provide: PermissionsStore, useValue: { setPermissions } },
      ],
    });

    return {
      facade: TestBed.inject(AccountFacade),
      httpMock: TestBed.inject(HttpTestingController),
      setInitialLanguage,
      setPermissions,
    };
  }

  it('loads /v1/me and feeds preferredLanguage + permissions into the two F5 hooks', async () => {
    const { facade, httpMock, setInitialLanguage, setPermissions } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush(me);
    await load;

    expect(facade.me()).toEqual(me);
    expect(facade.loading()).toBe(false);
    expect(facade.error()).toBeNull();
    expect(setInitialLanguage).toHaveBeenCalledWith('fr');
    expect(setPermissions).toHaveBeenCalledWith(['ASSET_TYPE:READ']);
  });

  it('exposes an error and does not call the F5 hooks when the request fails', async () => {
    const { facade, httpMock, setInitialLanguage, setPermissions } = setup();

    const load = facade.load();
    httpMock.expectOne('/api/platform/v1/me').flush(
      { detail: 'user_not_provisioned' },
      { status: 403, statusText: 'Forbidden' },
    );
    await load;

    expect(facade.me()).toBeNull();
    expect(facade.error()).toBe('user_not_provisioned');
    expect(setInitialLanguage).not.toHaveBeenCalled();
    expect(setPermissions).not.toHaveBeenCalled();
  });
});
