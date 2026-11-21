import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import type { RoleAssignment } from '../../../api/platform/models/role-assignment';
import type { RoleAssignmentListItem } from '../../../api/platform/models/role-assignment-list-item';
import { NX_SKIP_ERROR_NAVIGATION } from '../../../core/http/skip-error-navigation';
import { NxFormatService } from '../../../core/i18n/nx-format.service';
import { NX_NOW } from '../../../core/time/nx-clock';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { SYSTEM_USER_ID, SystemUserReadOnlyError } from '../../org/users/users.facade';
import { RoleAssignmentsFacade } from './role-assignments.facade';

const userId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0001';
const roleId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0002';
const leId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0020';
const buId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0021';
const whId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0022';

const item = (over: Partial<RoleAssignmentListItem>): RoleAssignmentListItem => ({
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0010',
  roleId,
  userId,
  scopeType: 'LEGAL_ENTITY',
  scopeEntityId: leId,
  source: 'MANUAL',
  validFrom: '2026-01-01T00:00:00Z',
  validTo: null,
  ...over,
});

const assignment = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0010',
  userId,
  roleId,
  scopeType: 'LEGAL_ENTITY',
  scopeEntityId: leId,
  validFrom: '2026-01-01T00:00:00Z',
  validTo: null,
} as RoleAssignment;

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('RoleAssignmentsFacade', () => {
  function setup(now?: number | (() => number)) {
    const activeLang = signal('es');
    TestBed.configureTestingModule({
      providers: [
        ...(now === undefined ? [] : [{ provide: NX_NOW, useValue: typeof now === 'function' ? now : () => now }]),
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
    return { facade: TestBed.inject(RoleAssignmentsFacade), httpMock: TestBed.inject(HttpTestingController), activeLang };
  }

  /** Answers the six requests a `list()` issues: the assignments and the five name lookups. */
  function answerList(httpMock: HttpTestingController, items: RoleAssignmentListItem[]): TestRequest[] {
    const requests = httpMock.match((r) => r.url.startsWith('/api/platform/v1/'));
    for (const req of requests) {
      const url = req.request.url;
      if (url.endsWith('/role-assignments')) {
        req.flush({ items, page: 1, pageSize: 200, total: items.length });
      } else if (url.endsWith('/roles')) {
        req.flush({ items: [{ id: roleId, code: 'ORG_ADMIN', captionI18n: { de: 'Org-Admin', es: 'Administrador de organización' }, isActive: true, isSystem: false }], page: 1, pageSize: 200, total: 1 });
      } else if (url.endsWith('/legal-entities')) {
        req.flush({ items: [{ id: leId, code: 'LE-CH', legalName: 'Nüssli (Schweiz) AG', isActive: true }], page: 1, pageSize: 100, total: 1 });
      } else if (url.endsWith('/business-units')) {
        req.flush({ items: [{ id: buId, code: 'BU-1', captionI18n: { de: 'Montage', es: 'Montaje' }, isActive: true }], page: 1, pageSize: 100, total: 1 });
      } else if (url.endsWith('/warehouses')) {
        req.flush({ items: [{ id: whId, code: 'WH-1', captionI18n: { de: 'Hauptlager', es: 'Almacén central' }, isActive: true }], page: 1, pageSize: 100, total: 1 });
      }
    }
    return requests;
  }

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('list() filters by userId and exposes rows resolved by name, never ids', async () => {
    const { facade, httpMock } = setup();
    const pending = facade.list(userId);
    const requests = answerList(httpMock, [
      item({ id: 'a1' }),
      item({ id: 'a2', scopeType: 'BUSINESS_UNIT', scopeEntityId: buId }),
      item({ id: 'a3', scopeType: 'WAREHOUSE', scopeEntityId: whId }),
      item({ id: 'a4', scopeType: 'GLOBAL', scopeEntityId: null }),
    ]);
    await pending;

    expect(requests.find((r) => r.request.url.endsWith('/role-assignments'))?.request.params.get('userId')).toBe(userId);
    const rows = facade.rows();
    expect(rows.map((r) => r.roleLabel)).toEqual(Array(4).fill('Administrador de organización'));
    expect(rows.map((r) => r.scopeLabel)).toEqual(['Nüssli (Schweiz) AG', 'Montaje', 'Almacén central', null]);
    expect(JSON.stringify(rows)).not.toContain(leId);
  });

  it('the four name lookups skip the /forbidden navigation; the assignments request does not', async () => {
    const { facade, httpMock } = setup();
    const pending = facade.list(userId);
    const requests = answerList(httpMock, [item({})]);
    await pending;

    const skip = (suffix: string) =>
      requests.find((r) => r.request.url.endsWith(suffix))?.request.context.get(NX_SKIP_ERROR_NAVIGATION);
    expect(['/roles', '/legal-entities', '/business-units', '/warehouses'].map(skip)).toEqual([true, true, true, true]);
    expect(skip('/role-assignments')).toBe(false);
  });

  it('create() refuses the SYSTEM user without a request', async () => {
    const { facade, httpMock } = setup();
    await expect(
      facade.create({ userId: SYSTEM_USER_ID, roleId, scopeType: 'GLOBAL' } as never),
    ).rejects.toBeInstanceOf(SystemUserReadOnlyError);
    httpMock.expectNone(() => true);
  });

  it('status follows the clock at each list(), not the first computation', async () => {
    let now = Date.parse('2026-06-01T00:00:00Z');
    const { facade: f, httpMock: http } = setup(() => now);
    const future = item({ validFrom: '2026-07-01T00:00:00Z' });
    let pending = f.list(userId);
    answerList(http, [future]);
    await pending;
    expect(f.rows()[0].status).toBe('pending');

    now = Date.parse('2026-08-01T00:00:00Z');
    pending = f.list(userId);
    answerList(http, [future]);
    await pending;
    expect(f.rows()[0]).toMatchObject({ status: 'active', canRevoke: true });
  });

  it('rows follow the active language', async () => {
    const { facade, httpMock, activeLang } = setup();
    const pending = facade.list(userId);
    answerList(httpMock, [item({ scopeType: 'BUSINESS_UNIT', scopeEntityId: buId })]);
    await pending;

    expect(facade.rows()[0].scopeLabel).toBe('Montaje');
    activeLang.set('de');
    expect(facade.rows()[0].scopeLabel).toBe('Montage');
    expect(facade.rows()[0].roleLabel).toBe('Org-Admin');
  });

  it('a failed name lookup shows «—» for those rows instead of failing the tab', async () => {
    const { facade, httpMock } = setup();
    const pending = facade.list(userId);
    for (const req of httpMock.match((r) => r.url.startsWith('/api/platform/v1/'))) {
      if (req.request.url.endsWith('/role-assignments')) {
        req.flush({ items: [item({})], page: 1, pageSize: 200, total: 1 });
      } else {
        req.flush({}, { status: 500, statusText: 'err' });
      }
    }
    await pending;

    expect(facade.error()).toBeNull();
    expect(facade.rows()[0]).toMatchObject({ roleLabel: '—', scopeLabel: '—' });
  });

  it('dates: validFrom by medium date; validTo shown as the last day (inclusive) when it is an exact midnight', async () => {
    const { facade, httpMock } = setup();
    TestBed.inject(NxFormatService);
    const pending = facade.list(userId);
    answerList(httpMock, [
      item({ id: 'a1', validFrom: '2026-11-14T00:00:00Z', validTo: '2026-12-01T00:00:00Z' }),
      item({ id: 'a2', validFrom: '2026-11-14T00:00:00Z', validTo: '2026-11-20T10:15:00Z' }),
    ]);
    await pending;

    const [exact, revoked] = facade.rows();
    expect(exact.validFromLabel).toContain('2026');
    expect(exact.validToLabel).toContain('30'); // 30 Nov: the day before the exclusive midnight
    expect(revoked.validToLabel).toContain('20');
  });

  it('status: pending when validFrom is in the future, active when valid now, inactive once ended; pending is not revocable', async () => {
    const { facade, httpMock } = setup(Date.parse('2026-11-18T12:00:00Z'));
    const pending = facade.list(userId);
    answerList(httpMock, [
      item({ id: 'future', validFrom: '2026-12-01T00:00:00Z' }),
      item({ id: 'now', validFrom: '2026-11-01T00:00:00Z' }),
      item({ id: 'past', validFrom: '2026-01-01T00:00:00Z', validTo: '2026-02-01T00:00:00Z' }),
    ]);
    await pending;

    const rows = Object.fromEntries(facade.rows().map((r) => [r.id, r]));
    expect(rows['future']).toMatchObject({ status: 'pending', isActive: false, canRevoke: false });
    expect(rows['now']).toMatchObject({ status: 'active', isActive: true, canRevoke: true });
    expect(rows['past']).toMatchObject({ status: 'inactive', isActive: false, canRevoke: false });
  });

  it('only MANUAL assignments that are still valid can be revoked (ACC-R6 protects SEED)', async () => {
    const { facade, httpMock } = setup();
    const pending = facade.list(userId);
    answerList(httpMock, [
      item({ id: 'manual' }),
      item({ id: 'seed', source: 'SEED' }),
      item({ id: 'ended', validTo: '2026-02-01T00:00:00Z' }),
    ]);
    await pending;

    expect(Object.fromEntries(facade.rows().map((r) => [r.id, r.canRevoke]))).toEqual({
      manual: true,
      seed: false,
      ended: false,
    });
  });

  it('a late list() of another user never overwrites the newer one', async () => {
    const { facade, httpMock } = setup();
    const first = facade.list('user-a');
    const firstRequests = httpMock.match((r) => r.url.startsWith('/api/platform/v1/'));
    const second = facade.list('user-b');
    answerList(httpMock, [item({ id: 'b-1' })]);
    await second;
    for (const req of firstRequests) {
      req.flush(req.request.url.endsWith('/role-assignments') ? { items: [item({ id: 'a-1' })], page: 1, pageSize: 200, total: 1 } : { items: [], page: 1, pageSize: 1, total: 0 });
    }
    await first;

    expect(facade.items().map((i) => i.id)).toEqual(['b-1']);
    expect(facade.loading()).toBe(false);
  });

  it('a failing assignments request sets a translated error and no rows', async () => {
    const { facade, httpMock } = setup();
    const pending = facade.list(userId);
    for (const req of httpMock.match((r) => r.url.startsWith('/api/platform/v1/'))) {
      req.flush({}, { status: 500, statusText: 'err' });
    }
    await pending;

    expect(facade.error()).not.toBeNull();
    expect(facade.rows()).toEqual([]);
  });

  it('revoke() reads the assignment for its ETag first, so ANY row can be revoked', async () => {
    const { facade, httpMock } = setup();

    const revoke = facade.revoke(assignment.id);
    httpMock.expectOne({ method: 'GET', url: `/api/platform/v1/role-assignments/${assignment.id}` }).flush(assignment, { headers: { ETag: '"v7"' } });
    await tick();
    const post = httpMock.expectOne(`/api/platform/v1/role-assignments/${assignment.id}/revoke`);
    expect(post.request.headers.get('If-Match')).toBe('"v7"');
    post.flush({ ...assignment, validTo: '2026-11-14T10:00:00Z' });

    expect((await revoke).validTo).toBe('2026-11-14T10:00:00Z');
    expect(facade.saving()).toBe(false);
  });

  it('create() sends an id, reuses it for a retry of the same request, and propagates the error', async () => {
    const { facade, httpMock } = setup();
    const request = { roleId, userId, scopeType: 'GLOBAL' as const, scopeEntityId: null, validFrom: '2026-11-14T00:00:00.000Z' };

    const first = facade.create(request);
    const r1 = httpMock.expectOne('/api/platform/v1/role-assignments');
    const id = (r1.request.body as { id: string }).id;
    r1.flush({ title: 'boom' }, { status: 500, statusText: 'err' });
    await expect(first).rejects.toBeInstanceOf(HttpErrorResponse);

    const retry = facade.create(request);
    const r2 = httpMock.expectOne('/api/platform/v1/role-assignments');
    expect((r2.request.body as { id: string }).id).toBe(id);
    r2.flush(assignment);
    await retry;
  });

  describe('describeError()', () => {
    const http = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

    it('409 CONCURRENCY: nothing to show', () => {
      const { facade } = setup();
      expect(facade.describeError(http(409, { code: 'CONCURRENCY' })).kind).toBe('concurrency');
    });

    it('409 CONFLICT is the overlapping-validity message', () => {
      const { facade } = setup();
      const info = facade.describeError(http(409, { code: 'CONFLICT' }));
      expect(info.kind).toBe('overlap');
      expect(info.message).toBe('org.users.detail.assignments.errors.overlap');
    });

    it('422 ACC-R1 and ACC-R2 go under the scope-entity field', () => {
      const { facade } = setup();
      for (const code of ['ACC-R1', 'ACC-R2']) {
        const info = facade.describeError(http(422, { code }));
        expect(info.message).toBeNull();
        expect(info.fields['scopeEntity']).toEqual([`errors.${code}`]);
      }
    });

    it('422 ACC-R4/R5/R6 are the translated alert', () => {
      const { facade } = setup();
      for (const code of ['ACC-R4', 'ACC-R5', 'ACC-R6']) {
        expect(facade.describeError(http(422, { code }))).toMatchObject({ kind: 'rule', message: `errors.${code}` });
      }
    });

    it('400 with [{field,message}] marks the control names (roleId → role)', () => {
      const { facade } = setup();
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const info = facade.describeError(
        http(400, { errors: [{ field: 'roleId', message: 'x' }, { field: 'validTo', message: 'y' }] }),
      );
      expect(info.kind).toBe('validation');
      expect(info.message).toBe('errors.validation');
      expect(Object.keys(info.fields).sort()).toEqual(['role', 'validTo']);
      warn.mockRestore();
    });
  });
});
