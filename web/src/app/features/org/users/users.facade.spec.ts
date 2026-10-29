import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import type { User } from '../../../api/platform/models/user';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { UsersFacade } from './users.facade';

const user: User = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c80',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c13',
  fullName: 'Jane Doe',
  email: 'jane.doe@nussli.ch',
  preferredLanguage: 'en',
  businessUnitId: null,
  oid: 'oid-1',
  isActive: true,
};

describe('UsersFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null) => code ?? 'generic' } },
      ],
    });

    return {
      facade: TestBed.inject(UsersFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('list() maps the 1-based API page response to the 0-based grid Page envelope', async () => {
    const { facade, httpMock } = setup();

    const items: UserListItem[] = [
      { id: user.id, fullName: user.fullName, email: user.email, preferredLanguage: 'en', businessUnitId: null, isActive: true },
    ];

    const result = firstValueFrom(facade.list(0, 25, null));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/users');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ items, page: 1, pageSize: 25, total: 1 });

    const page = await result;
    expect(page).toEqual({ items, page: 0, pageSize: 25, total: 1 });
  });

  it('load() captures the body and the ETag header', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(user.id);
    httpMock.expectOne(`/api/platform/v1/users/${user.id}`).flush(user, { headers: { ETag: '"v1"' } });
    await load;

    expect(facade.selected()).toEqual(user);
    expect(facade.error()).toBeNull();
  });

  it('create() attributes an ORG-R9 (duplicate email) 422 to the email field', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({ oid: 'new-oid', fullName: 'New User', preferredLanguage: 'de' });
    httpMock.expectOne('/api/platform/v1/users').flush(
      { code: 'ORG-R9', detail: 'Email already in use' },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    const result = await create;
    expect(result).toBeNull();
    expect(facade.fieldErrors()).toEqual({ email: 'ORG-R9' });
    expect(facade.error()).toBeNull();
  });

  it('update() sends the ETag captured by load() as If-Match', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(user.id);
    httpMock.expectOne(`/api/platform/v1/users/${user.id}`).flush(user, { headers: { ETag: '"v1"' } });
    await load;

    const update = facade.update(user.id, {
      fullName: 'Jane Updated',
      preferredLanguage: 'en',
      email: user.email,
      businessUnitId: null,
    });
    const req = httpMock.expectOne(`/api/platform/v1/users/${user.id}`);
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    req.flush({ ...user, fullName: 'Jane Updated' }, { headers: { ETag: '"v2"' } });

    const result = await update;
    expect(result?.fullName).toBe('Jane Updated');
  });

  it('deactivate() returns the deactivated user on success', async () => {
    const { facade, httpMock } = setup();

    const deactivate = facade.deactivate(user.id);
    httpMock.expectOne(`/api/platform/v1/users/${user.id}/deactivate`).flush({ ...user, isActive: false });

    const result = await deactivate;
    expect(result?.isActive).toBe(false);
  });

  it('searchBusinessUnits() filters the fetched page client-side by code/caption', async () => {
    const { facade, httpMock } = setup();

    const search = firstValueFrom(facade.searchBusinessUnits('ops'));
    httpMock.expectOne((r) => r.url === '/api/platform/v1/business-units').flush({
      items: [
        { id: 'bu-1', code: 'OPS-01', isActive: true, legalEntityId: 'le-1', captionI18n: { de: 'Operations' } },
        { id: 'bu-2', code: 'FIN-01', isActive: true, legalEntityId: 'le-1', captionI18n: { de: 'Finance' } },
      ],
      page: 1,
      pageSize: 100,
      total: 2,
    });

    const results = await search;
    expect(results).toHaveLength(1);
    expect(results[0].code).toBe('OPS-01');
  });
});
