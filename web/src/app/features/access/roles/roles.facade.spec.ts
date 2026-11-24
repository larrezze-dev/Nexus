import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Role } from '../../../api/platform/models/role';
import { RolesFacade } from './roles.facade';

const role: Role = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'ADMIN',
  captionI18n: { de: 'Administrator' },
  description: null,
  isSystem: false,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};
const url = `/api/platform/v1/roles/${role.id}`;

describe('RolesFacade', () => {
  let facade: RolesFacade;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    facade = TestBed.inject(RolesFacade);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('a late load response does not overwrite a newer one', async () => {
    const first = facade.load(role.id);
    const second = facade.load(role.id);
    const [slow, fast] = http.match(url);
    fast.flush({ ...role, code: 'NEW' }, { headers: { ETag: '"2"' } });
    await second;
    slow.flush({ ...role, code: 'OLD' }, { headers: { ETag: '"1"' } });
    await first;

    expect(facade.selected()?.code).toBe('NEW');
    expect(facade.loading()).toBe(false);
  });

  it('a response arriving after reset() is ignored', async () => {
    const pending = facade.load(role.id);
    facade.reset();
    http.expectOne(url).flush(role);
    await pending;

    expect(facade.selected()).toBeNull();
  });

  it('a failed load sets the error and clears the selection', async () => {
    const pending = facade.load(role.id);
    http.expectOne(url).flush({ title: 'x' }, { status: 500, statusText: 'err' });
    await pending;

    expect(facade.error()).toBe('load_failed');
    expect(facade.selected()).toBeNull();
  });

  it('sends If-Match on the permission save and keeps the ETag of the response for the next write', async () => {
    const load = facade.load(role.id);
    http.expectOne(url).flush(role, { headers: { ETag: '"1"' } });
    await load;

    const save = facade.savePermissions(role.id, ['AUDIT:READ']);
    const put = http.expectOne(`${url}/permissions`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('If-Match')).toBe('"1"');
    expect(put.request.body).toEqual({ permissionCodes: ['AUDIT:READ'] });
    put.flush([], { headers: { ETag: '"2"' } });
    await save;
    // no re-read of the role: the header was enough
    http.expectNone(url);

    const update = facade.update(role.id, { captionI18n: role.captionI18n, description: null });
    const patch = http.expectOne(url);
    expect(patch.request.headers.get('If-Match')).toBe('"2"');
    patch.flush(role, { headers: { ETag: '"3"' } });
    await update;
  });

  it('re-reads the role first when no ETag is held, so the permission save always sends If-Match', async () => {
    const save = facade.savePermissions(role.id, ['AUDIT:READ']);
    http.expectOne(url).flush(role, { headers: { ETag: '"7"' } });
    const put = await vi.waitFor(() => {
      const [request] = http.match(`${url}/permissions`);
      if (!request) throw new Error('permission save not sent yet');
      return request;
    });
    expect(put.request.headers.get('If-Match')).toBe('"7"');
    put.flush([], { headers: { ETag: '"8"' } });
    await save;
  });

  it('two permission saves in a row use the ETag returned by the first', async () => {
    const load = facade.load(role.id);
    http.expectOne(url).flush(role, { headers: { ETag: '"1"' } });
    await load;

    const first = facade.savePermissions(role.id, []);
    http.expectOne(`${url}/permissions`).flush([], { headers: { ETag: '"2"' } });
    await first;
    const second = facade.savePermissions(role.id, []);
    const put = http.expectOne(`${url}/permissions`);
    expect(put.request.headers.get('If-Match')).toBe('"2"');
    put.flush([], { headers: { ETag: '"3"' } });
    await second;
  });

  it('falls back to re-reading the role when the permission save answers no ETag', async () => {
    const load = facade.load(role.id);
    http.expectOne(url).flush(role, { headers: { ETag: '"1"' } });
    await load;

    const save = facade.savePermissions(role.id, []);
    http.expectOne(`${url}/permissions`).flush([]);
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne(url).flush(role, { headers: { ETag: '"2"' } });
    await save;

    const update = facade.update(role.id, { captionI18n: role.captionI18n, description: null });
    const patch = http.expectOne(url);
    expect(patch.request.headers.get('If-Match')).toBe('"2"');
    patch.flush(role, { headers: { ETag: '"3"' } });
    await update;
  });

  it('propagates a 409 from the permission save untouched', async () => {
    const load = facade.load(role.id);
    http.expectOne(url).flush(role, { headers: { ETag: '"1"' } });
    await load;
    const save = facade.savePermissions(role.id, []);
    http.expectOne(`${url}/permissions`).flush(
      { code: 'CONCURRENCY', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );
    await expect(save).rejects.toMatchObject({ status: 409 });
  });
});
