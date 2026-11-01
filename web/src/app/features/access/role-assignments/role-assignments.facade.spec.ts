import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import type { RoleAssignment } from '../../../api/platform/models/role-assignment';
import type { RoleAssignmentListItem } from '../../../api/platform/models/role-assignment-list-item';
import { RoleAssignmentsFacade } from './role-assignments.facade';

const userId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0001';
const roleId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0002';

const listItem: RoleAssignmentListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0010',
  roleId,
  userId,
  scopeType: 'LEGAL_ENTITY',
  scopeEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0020',
  source: 'MANUAL',
  validFrom: '2026-01-01T00:00:00Z',
  validTo: null,
};

const assignment: RoleAssignment = {
  id: listItem.id,
  roleId,
  userId,
  scopeType: 'LEGAL_ENTITY',
  scopeEntityId: listItem.scopeEntityId,
  source: 'MANUAL',
  sourceRefId: null,
  validFrom: listItem.validFrom,
  validTo: null,
  grantedBy: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0099',
  revokedBy: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('RoleAssignmentsFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return {
      facade: TestBed.inject(RoleAssignmentsFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('list() filters by userId and stores the items', async () => {
    const { facade, httpMock } = setup();

    const result = facade.list(userId);
    const req = httpMock.expectOne(
      (r) => r.url === '/api/platform/v1/role-assignments' && r.params.get('userId') === userId,
    );
    req.flush({ items: [listItem], page: 1, pageSize: 200, total: 1 });

    expect(await result).toEqual([listItem]);
    expect(facade.items()).toEqual([listItem]);
  });

  it('canRevoke() is false for an assignment this facade never created (contract gap)', () => {
    const { facade } = setup();
    expect(facade.canRevoke(listItem.id)).toBe(false);
  });

  it('create() captures the response ETag keyed by the created assignment id, enabling canRevoke()', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({
      roleId,
      userId,
      scopeType: 'LEGAL_ENTITY',
      scopeEntityId: assignment.scopeEntityId,
      validFrom: assignment.validFrom,
    });
    const req = httpMock.expectOne('/api/platform/v1/role-assignments');
    expect(req.request.method).toBe('POST');
    req.flush(assignment, { status: 201, statusText: 'Created', headers: { ETag: '"1"' } });

    const created = await create;
    expect(created).toEqual(assignment);
    expect(facade.canRevoke(assignment.id)).toBe(true);
  });

  it('revoke() sends If-Match with the ETag captured by create() and clears it afterwards', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({
      roleId,
      userId,
      scopeType: 'LEGAL_ENTITY',
      scopeEntityId: assignment.scopeEntityId,
      validFrom: assignment.validFrom,
    });
    httpMock
      .expectOne('/api/platform/v1/role-assignments')
      .flush(assignment, { status: 201, statusText: 'Created', headers: { ETag: '"1"' } });
    await create;

    const revoke = facade.revoke(assignment.id);
    const req = httpMock.expectOne(`/api/platform/v1/role-assignments/${assignment.id}/revoke`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    req.flush({ ...assignment, revokedBy: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0099' }, { headers: { ETag: '"2"' } });

    await revoke;
    expect(facade.canRevoke(assignment.id)).toBe(false);
  });

  it('getEffectiveScope() wraps GET /v1/users/{id}/effective-scope', async () => {
    const { facade, httpMock } = setup();

    const result = facade.getEffectiveScope(userId);
    const req = httpMock.expectOne(`/api/platform/v1/users/${userId}/effective-scope`);
    req.flush({ legalEntityIds: ['le-1'], businessUnitIds: [], warehouseIds: [], projectIds: [] });

    expect(await result).toEqual({
      legalEntityIds: ['le-1'],
      businessUnitIds: [],
      warehouseIds: [],
      projectIds: [],
    });
  });

  it('searchRoles() sends the query as the server-side q param', async () => {
    const { facade, httpMock } = setup();

    const result = facade.searchRoles('adm');
    const req = httpMock.expectOne(
      (r) => r.url === '/api/platform/v1/roles' && r.params.get('q') === 'adm',
    );
    req.flush({
      items: [{ id: roleId, code: 'ADMIN', captionI18n: { de: 'Admin' }, isActive: true, isSystem: false }],
      page: 1,
      pageSize: 100,
      total: 1,
    });

    const [role] = await firstValueFrom(result);
    expect(role.code).toBe('ADMIN');
  });

  it('searchLegalEntities() filters the fetched page by a case-insensitive code substring', async () => {
    const { facade, httpMock } = setup();

    const result = facade.searchLegalEntities('ch');
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    req.flush({
      items: [
        { id: 'le-1', groupId: 'g-1', code: 'LE-CH', currencyCode: 'CHF', countryCode: 'CH', isActive: true },
        { id: 'le-2', groupId: 'g-1', code: 'LE-FR', currencyCode: 'EUR', countryCode: 'FR', isActive: true },
      ],
      page: 1,
      pageSize: 100,
      total: 2,
    });

    const items = await firstValueFrom(result);
    expect(items.map((le) => le.code)).toEqual(['LE-CH']);
  });
});
