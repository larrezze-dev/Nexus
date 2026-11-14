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
  legalName: 'SAPE smart bud AG',
  currencyCode: legalEntity.currencyCode,
  countryCode: legalEntity.countryCode,
  isActive: true,
};

const party = {
  id: legalEntity.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud AG',
  displayName: 'SSB Schweiz',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

/** Answers the GET of `load()` and then the GET of its party (sent once the entity arrived). */
async function flushLoad(httpMock: HttpTestingController, etag: string): Promise<void> {
  httpMock
    .expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`)
    .flush(legalEntity, { headers: { ETag: etag } });
  await new Promise((resolve) => setTimeout(resolve));
  httpMock.expectOne(`/api/platform/v1/parties/${legalEntity.partyId}`).flush(party);
}

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

  it('list() sends the search text and the status filter', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(0, 25, null, { q: '  sape ', isActive: false }));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    expect(req.request.params.get('q')).toBe('sape');
    expect(req.request.params.get('isActive')).toBe('false');
    req.flush({ items: [], page: 1, pageSize: 25, total: 0 });
    await result;
  });

  it('list() leaves out an empty search and an unset status', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(0, 25, null, { q: '  ' }));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    expect(req.request.params.has('q')).toBe(false);
    expect(req.request.params.has('isActive')).toBe(false);
    req.flush({ items: [], page: 1, pageSize: 25, total: 0 });
    await result;
  });

  it('groupOptions() lists the groups with the legal name of their party', async () => {
    const { facade, httpMock } = setup();

    const result = facade.groupOptions();
    httpMock
      .expectOne((r) => r.url === '/api/platform/v1/groups')
      .flush({ items: [{ id: 'g1', code: 'SAPE', partyId: legalEntity.partyId, reportingCurrencyCode: 'CHF' }], page: 1, pageSize: 200, total: 1 });
    await new Promise((resolve) => setTimeout(resolve));
    httpMock.expectOne(`/api/platform/v1/parties/${legalEntity.partyId}`).flush(party);

    expect(await result).toEqual([{ id: 'g1', code: 'SAPE', name: 'SAPE smart bud AG' }]);
  });

  it('search() lets the server filter with q (no client-side filtering)', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.search(' ch '));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    expect(req.request.params.get('q')).toBe('ch');
    req.flush({ items: [listItem], page: 1, pageSize: 100, total: 1 });

    expect(await result).toEqual([listItem]);
  });

  it('search() sends no q for an empty query', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.search('  '));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/legal-entities');
    expect(req.request.params.has('q')).toBe(false);
    req.flush({ items: [], page: 1, pageSize: 100, total: 0 });

    expect(await result).toEqual([]);
  });

  it('load() stores the entity and the response ETag', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    await flushLoad(httpMock, '"1"');
    await load;

    expect(facade.selected()).toEqual(legalEntity);
    expect(facade.party()?.legalName).toBe('SAPE smart bud AG');
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
    await flushLoad(httpMock, '"7"');
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
    await flushLoad(httpMock, '"1"');
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
    await flushLoad(httpMock, '"1"');
    await load;

    facade.reset();

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBeNull();
  });

  it('load(): a late response of an older load never overwrites the newer one', async () => {
    const { facade, httpMock } = setup();
    const older = { ...legalEntity, id: 'le-old', partyId: 'p-old', code: 'OLD' };
    const newer = { ...legalEntity, id: 'le-new', partyId: 'p-new', code: 'NEW' };

    const first = facade.load(older.id);
    const second = facade.load(newer.id);
    httpMock.expectOne('/api/platform/v1/legal-entities/le-new').flush(newer, { headers: { ETag: '"new"' } });
    await new Promise((resolve) => setTimeout(resolve));
    httpMock.expectOne('/api/platform/v1/parties/p-new').flush(party);
    await second;
    // The older answer arrives last: it is dropped (and does not even fetch its party).
    httpMock.expectOne('/api/platform/v1/legal-entities/le-old').flush(older, { headers: { ETag: '"old"' } });
    await first;

    expect(facade.selected()?.code).toBe('NEW');
    expect(facade.loading()).toBe(false);
    httpMock.verify();
  });

  it('load(): the party of a superseded load is dropped too', async () => {
    const { facade, httpMock } = setup();
    const older = { ...legalEntity, id: 'le-old', partyId: 'p-old', code: 'OLD' };
    const newer = { ...legalEntity, id: 'le-new', partyId: 'p-new', code: 'NEW' };

    const first = facade.load(older.id);
    httpMock.expectOne('/api/platform/v1/legal-entities/le-old').flush(older);
    await new Promise((resolve) => setTimeout(resolve));
    const oldParty = httpMock.expectOne('/api/platform/v1/parties/p-old');
    const second = facade.load(newer.id);
    httpMock.expectOne('/api/platform/v1/legal-entities/le-new').flush(newer);
    await new Promise((resolve) => setTimeout(resolve));
    httpMock.expectOne('/api/platform/v1/parties/p-new').flush({ ...party, legalName: 'New AG' });
    await second;
    oldParty.flush({ ...party, legalName: 'Old AG' });
    await first;

    expect(facade.party()?.legalName).toBe('New AG');
  });

  it('reset() invalidates a load in flight: its response is ignored and loading() is false', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(legalEntity.id);
    expect(facade.loading()).toBe(true);
    facade.reset();
    expect(facade.loading()).toBe(false);
    httpMock.expectOne(`/api/platform/v1/legal-entities/${legalEntity.id}`).flush(legalEntity);
    await load;

    expect(facade.selected()).toBeNull();
    expect(facade.party()).toBeNull();
    httpMock.verify();
  });
});
