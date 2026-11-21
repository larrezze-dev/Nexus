import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { NX_SKIP_ERROR_NAVIGATION } from '../../../core/http/skip-error-navigation';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import type { User } from '../../../api/platform/models/user';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { SYSTEM_USER_ID, SystemUserReadOnlyError } from '../../../shared/auth/system-user';
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
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};
const otherUser: User = { ...user, id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c81', fullName: 'John Roe' };
const businessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c90',
  code: 'BU-1',
  captionI18n: { de: 'Montage', es: 'Montaje' },
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c91',
  isActive: true,
};

const userUrl = (id: string) => `/api/platform/v1/users/${id}`;
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('UsersFacade', () => {
  function setup(language = 'de') {
    const activeLang = signal(language);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        {
          provide: NxI18nService,
          useValue: {
            translateErrorCode: (code: string | null) => (code ? `errors.${code}` : 'errors.generic'),
            getActiveLanguage: () => activeLang(),
            activeLang,
          },
        },
      ],
    });
    return {
      facade: TestBed.inject(UsersFacade),
      httpMock: TestBed.inject(HttpTestingController),
      activeLang,
    };
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

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

    expect(await result).toEqual({ items, page: 0, pageSize: 25, total: 1 });
  });

  it('languageName() reads the language by name in the active language, never the code', () => {
    const { facade, activeLang } = setup('es');
    expect(facade.languageName('de')).toBe('Alemán');
    activeLang.set('de');
    expect(facade.languageName('es')).toBe('Spanisch');
    expect(facade.languageName(null)).toBe('');
  });

  it('businessUnits() and searchBusinessUnits() skip the /forbidden navigation (a 403 must not leave /users)', async () => {
    const { facade, httpMock } = setup();
    for (const make of [() => facade.businessUnits(), () => facade.searchBusinessUnits('x')]) {
      const search = make();
      const done = new Promise((resolve) => search.subscribe({ next: resolve, error: resolve }));
      const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/business-units');
      expect(req.request.context.get(NX_SKIP_ERROR_NAVIGATION)).toBe(true);
      req.flush({}, { status: 403, statusText: 'Forbidden' });
      await done;
    }
  });

  it('load() captures the body, the ETag and resolves the business unit by name', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(user.id);
    httpMock
      .expectOne(userUrl(user.id))
      .flush({ ...user, businessUnitId: businessUnit.id }, { headers: { ETag: '"v1"' } });
    await tick();
    const buRequest = httpMock.expectOne(`/api/platform/v1/business-units/${businessUnit.id}`);
    // A secondary name lookup must not send the user to /forbidden on a 403.
    expect(buRequest.request.context.get(NX_SKIP_ERROR_NAVIGATION)).toBe(true);
    buRequest.flush(businessUnit);
    await load;

    expect(facade.selected()?.fullName).toBe('Jane Doe');
    expect(facade.title()).toBe('Jane Doe');
    expect(facade.resolvedBusinessUnitLabel()).toBe('BU-1 — Montage');
    expect(facade.error()).toBeNull();
    expect(facade.isSystem()).toBe(false);
  });

  it('a late response of an older load() never overwrites the newer one', async () => {
    const { facade, httpMock } = setup();

    const first = facade.load(user.id);
    const second = facade.load(otherUser.id);
    httpMock.expectOne(userUrl(otherUser.id)).flush(otherUser);
    await second;
    httpMock.expectOne(userUrl(user.id)).flush(user);
    await first;

    expect(facade.selected()?.id).toBe(otherUser.id);
    expect(facade.loading()).toBe(false);
  });

  it('a late failure of an older load() leaves no error behind', async () => {
    const { facade, httpMock } = setup();

    const first = facade.load(user.id);
    const second = facade.load(otherUser.id);
    httpMock.expectOne(userUrl(otherUser.id)).flush(otherUser);
    await second;
    httpMock.expectOne(userUrl(user.id)).flush({ code: 'x' }, { status: 500, statusText: 'err' });
    await first;

    expect(facade.error()).toBeNull();
    expect(facade.selected()?.id).toBe(otherUser.id);
  });

  it('reset() drops a load still in flight', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(user.id);
    facade.reset();
    httpMock.expectOne(userUrl(user.id)).flush(user);
    await load;

    expect(facade.selected()).toBeNull();
  });

  it('a failed load() keeps selected null and exposes the error so the page can offer Retry', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(user.id);
    httpMock.expectOne(userUrl(user.id)).flush({ title: 'boom' }, { status: 500, statusText: 'err' });
    await load;

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBe('errors.generic');
  });

  it('update() sends the loaded ETag as If-Match and propagates the HTTP error', async () => {
    const { facade, httpMock } = setup();
    const load = facade.load(user.id);
    httpMock.expectOne(userUrl(user.id)).flush(user, { headers: { ETag: '"v1"' } });
    await load;

    const update = facade.update(user.id, { fullName: 'Jane D.', preferredLanguage: 'en' });
    const req = httpMock.expectOne(userUrl(user.id));
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    req.flush({ code: 'CONCURRENCY' }, { status: 409, statusText: 'Conflict' });

    const error = await update.then(
      () => null,
      (e: unknown) => e,
    );
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(facade.saving()).toBe(false);
  });

  it('create() sends a client-generated id and reuses it for a retry of the same content', async () => {
    const { facade, httpMock } = setup();
    const request = { oid: 'oid-9', fullName: 'New', preferredLanguage: 'de' as const };

    const first = facade.create(request).catch(() => null);
    const r1 = httpMock.expectOne('/api/platform/v1/users');
    const id = (r1.request.body as { id: string }).id;
    expect(id).toMatch(/^[0-9a-f-]{36}$/);
    r1.flush({ code: 'x' }, { status: 500, statusText: 'err' });
    await first;

    const retry = facade.create(request);
    const r2 = httpMock.expectOne('/api/platform/v1/users');
    expect((r2.request.body as { id: string }).id).toBe(id);
    r2.flush(user);
    expect((await retry).id).toBe(user.id);
  });

  describe('ORG-R6: SYSTEM user is read-only for real', () => {
    it('update() and deactivate() refuse before any request', async () => {
      const { facade, httpMock } = setup();

      await expect(facade.update(SYSTEM_USER_ID, { fullName: 'x', preferredLanguage: 'de' })).rejects.toBeInstanceOf(
        SystemUserReadOnlyError,
      );
      await expect(facade.deactivate(SYSTEM_USER_ID)).rejects.toBeInstanceOf(SystemUserReadOnlyError);
      httpMock.expectNone(() => true);
    });

    it('isSystem() is true once the SYSTEM user is loaded', async () => {
      const { facade, httpMock } = setup();
      const load = facade.load(SYSTEM_USER_ID);
      httpMock.expectOne(userUrl(SYSTEM_USER_ID)).flush({ ...user, id: SYSTEM_USER_ID, fullName: 'SYSTEM', oid: null });
      await load;

      expect(facade.isSystem()).toBe(true);
    });

    it('describeError() gives the translated ORG-R6 rule text', () => {
      const { facade } = setup();
      expect(facade.describeError(new SystemUserReadOnlyError())).toMatchObject({
        kind: 'rule',
        message: 'errors.ORG-R6',
      });
    });
  });

  describe('describeError()', () => {
    const http = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

    it('409 CONCURRENCY: nothing to show (the interceptor already did)', () => {
      const { facade } = setup();
      expect(facade.describeError(http(409, { code: 'CONCURRENCY' }))).toEqual({ kind: 'concurrency', message: null, fields: {} });
    });

    it('409 CONFLICT marks the oid field', () => {
      const { facade } = setup();
      const info = facade.describeError(http(409, { code: 'CONFLICT' }));
      expect(info.kind).toBe('duplicate');
      expect(info.fields['oid']).toEqual(['org.users.errors.oidTaken']);
    });

    it('422 ORG-R9 marks the email field', () => {
      const { facade } = setup();
      const info = facade.describeError(http(422, { code: 'ORG-R9' }));
      expect(info.fields['email']).toEqual(['errors.ORG-R9']);
    });

    it('400 with the real [{field,message}] shape marks those fields and says errors.validation', () => {
      const { facade } = setup();
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const info = facade.describeError(
        http(400, { errors: [{ field: 'FullName', message: 'must not be empty' }, { field: 'businessUnitId', message: 'bad' }] }),
      );
      expect(info.kind).toBe('validation');
      expect(info.message).toBe('errors.validation');
      expect(Object.keys(info.fields).sort()).toEqual(['businessUnit', 'fullName']);
      warn.mockRestore();
    });

    it('other failures give the generic message', () => {
      const { facade } = setup();
      expect(facade.describeError(http(500, null))).toMatchObject({ kind: 'generic', message: 'errors.generic' });
    });
  });
});
