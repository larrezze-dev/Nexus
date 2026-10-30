import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LegalEntitiesFacade } from './legal-entities.facade';

const legalEntity: LegalEntity = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c11',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const listItem: LegalEntityListItem = {
  id: legalEntity.id,
  groupId: legalEntity.groupId,
  code: legalEntity.code,
  currencyCode: legalEntity.currencyCode,
  countryCode: legalEntity.countryCode,
  isActive: true,
};

describe('LegalEntitiesFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return {
      facade: TestBed.inject(LegalEntitiesFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('list() converts the 0-based grid page to the 1-based API page and back', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(2, 25, { colId: 'code', direction: 'desc' }));
    const req = httpMock.expectOne(
      (r) => r.url === '/api/platform/v1/legal-entities' && r.params.get('page') === '3',
    );
    expect(req.request.params.get('pageSize')).toBe('25');
    expect(req.request.params.get('sort')).toBe('-code');
    req.flush({ items: [listItem], page: 3, pageSize: 25, total: 1 });

    const page = await result;
    expect(page).toEqual({ items: [listItem], page: 2, pageSize: 25, total: 1 });
  });

  it('search() filters the fetched page by a case-insensitive code substring', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.search('ch'));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    req.flush({
      items: [listItem, { ...listItem, id: 'other', code: 'LE-FR' }],
      page: 1,
      pageSize: 100,
      total: 2,
    });

    expect(await result).toEqual([listItem]);
  });

  it('load() stores the entity and the response ETag', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    httpMock
      .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
      .flush(legalEntity, { headers: { ETag: '"1"' } });
    await load;

    expect(facade.selected()).toEqual(legalEntity);
    expect(facade.loading()).toBe(false);
    expect(facade.error()).toBeNull();
  });

  it('load() sets an error and clears selected() when the request fails', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    httpMock
      .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
      .flush({ detail: 'not found' }, { status: 404, statusText: 'Not Found' });
    await load;

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBe('load_failed');
  });

  it('create() posts the request and stores the created entity + ETag', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({
      legalName: 'Nexus Event AG',
      groupId: legalEntity.groupId,
      code: legalEntity.code,
      currencyCode: legalEntity.currencyCode,
      countryCode: legalEntity.countryCode,
    });
    const req = httpMock.expectOne('/api/platform/v1/legal-entities');
    expect(req.request.method).toBe('POST');
    req.flush(legalEntity, { status: 201, statusText: 'Created', headers: { ETag: '"1"' } });

    expect(await create).toEqual(legalEntity);
    expect(facade.selected()).toEqual(legalEntity);
  });

  it('update() sends If-Match with the ETag captured by the last load()', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    httpMock
      .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
      .flush(legalEntity, { headers: { ETag: '"7"' } });
    await load;

    const update = facade.update(legalEntity.id, {
      code: legalEntity.code,
      currencyCode: legalEntity.currencyCode,
      countryCode: 'DE',
    });
    const req = httpMock.expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"7"');
    req.flush({ ...legalEntity, countryCode: 'DE' }, { headers: { ETag: '"8"' } });

    const updated = await update;
    expect(updated.countryCode).toBe('DE');
  });

  it('deactivate() sends If-Match and stores the deactivated entity', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    httpMock
      .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
      .flush(legalEntity, { headers: { ETag: '"1"' } });
    await load;

    const deactivate = facade.deactivate(legalEntity.id);
    const req = httpMock.expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}/deactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    req.flush({ ...legalEntity, isActive: false }, { headers: { ETag: '"2"' } });

    expect((await deactivate).isActive).toBe(false);
    expect(facade.selected()?.isActive).toBe(false);
  });

  it('reset() clears selected(), error() and the stored ETag', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    httpMock
      .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
      .flush(legalEntity, { headers: { ETag: '"1"' } });
    await load;

    facade.reset();

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBeNull();
  });
});
