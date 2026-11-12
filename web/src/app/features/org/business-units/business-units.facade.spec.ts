import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { Party } from '../../../api/platform/models/party';
import { BusinessUnitsFacade } from './business-units.facade';

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-01',
  captionI18n: { de: 'Vermietung', en: 'Rental' },
  costCenterCode: 'CC-100',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const listItem: BusinessUnitListItem = {
  id: businessUnit.id,
  legalEntityId: businessUnit.legalEntityId,
  code: businessUnit.code,
  captionI18n: businessUnit.captionI18n,
  isActive: true,
};

const party: Party = {
  id: businessUnit.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud Rental',
  displayName: 'SSB Rental',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: businessUnit.createdAt,
  updatedAt: businessUnit.updatedAt,
};

const legalEntityItem = {
  id: businessUnit.legalEntityId,
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-SSB',
  legalName: 'SAPE smart bud AG',
  displayName: null,
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

/** Answers the GET of `load()` and then the GET of its party (sent once the unit arrived). */
async function flushLoad(httpMock: HttpTestingController, etag: string): Promise<void> {
  httpMock
    .expectOne(`/api/platform/v1/business-units/${businessUnit.id}`)
    .flush(businessUnit, { headers: { ETag: etag } });
  await new Promise((resolve) => setTimeout(resolve));
  httpMock.expectOne(`/api/platform/v1/parties/${businessUnit.partyId}`).flush(party);
}

describe('BusinessUnitsFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return {
      facade: TestBed.inject(BusinessUnitsFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('list() converts the 0-based grid page to the 1-based API page and adds each legal entity', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(1, 25, { colId: 'isActive', direction: 'desc' }));
    const req = httpMock.expectOne(
      (r) => r.url === '/api/platform/v1/business-units' && r.params.get('page') === '2',
    );
    expect(req.request.params.get('sort')).toBe('-isActive');
    req.flush({ items: [listItem, { ...listItem, id: 'other', legalEntityId: 'unknown' }], page: 2, pageSize: 25, total: 27 });
    await new Promise((resolve) => setTimeout(resolve));
    httpMock
      .expectOne((r) => r.url === '/api/platform/v1/legal-entities')
      .flush({ items: [legalEntityItem], page: 1, pageSize: 200, total: 1 });

    expect(await result).toEqual({
      items: [
        { ...listItem, legalEntity: { id: legalEntityItem.id, code: 'LE-SSB', legalName: 'SAPE smart bud AG', isActive: true } },
        { ...listItem, id: 'other', legalEntityId: 'unknown', legalEntity: null },
      ],
      page: 1,
      pageSize: 25,
      total: 27,
    });
  });

  it('list() still returns the rows when the legal entities cannot be read', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(0, 25, null));
    httpMock.expectOne((r) => r.url === '/api/platform/v1/business-units').flush({ items: [listItem], page: 1, pageSize: 25, total: 1 });
    await new Promise((resolve) => setTimeout(resolve));
    httpMock
      .expectOne((r) => r.url === '/api/platform/v1/legal-entities')
      .flush(null, { status: 403, statusText: 'Forbidden' });

    expect((await result).items).toEqual([{ ...listItem, legalEntity: null }]);
  });

  it('load() stores the unit, the response ETag and its party', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(businessUnit.id);
    await flushLoad(httpMock, '"1"');
    await load;

    expect(facade.selected()).toEqual(businessUnit);
    expect(facade.party()).toEqual(party);
  });

  it('create() posts the request and stores the created entity', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({
      legalName: 'Vermietung CH',
      legalEntityId: businessUnit.legalEntityId,
      code: businessUnit.code,
      captionI18n: businessUnit.captionI18n,
      costCenterCode: 'CC-100',
    });
    const req = httpMock.expectOne('/api/platform/v1/business-units');
    expect(req.request.method).toBe('POST');
    req.flush(businessUnit, { status: 201, statusText: 'Created', headers: { ETag: '"1"' } });

    expect(await create).toEqual(businessUnit);
  });

  it('update() sends If-Match with the ETag captured by the last load()', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(businessUnit.id);
    await flushLoad(httpMock, '"7"');
    await load;

    const update = facade.update(businessUnit.id, {
      code: businessUnit.code,
      captionI18n: { de: 'Verleih' },
      costCenterCode: 'CC-100',
    });
    const req = httpMock.expectOne(`/api/platform/v1/business-units/${businessUnit.id}`);
    expect(req.request.headers.get('If-Match')).toBe('"7"');
    req.flush({ ...businessUnit, captionI18n: { de: 'Verleih' } }, { headers: { ETag: '"8"' } });

    expect((await update).captionI18n.de).toBe('Verleih');
  });

  it('deactivate() sends If-Match and stores the deactivated entity', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(businessUnit.id);
    await flushLoad(httpMock, '"1"');
    await load;

    const deactivate = facade.deactivate(businessUnit.id);
    const req = httpMock.expectOne(`/api/platform/v1/business-units/${businessUnit.id}/deactivate`);
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    req.flush({ ...businessUnit, isActive: false }, { headers: { ETag: '"2"' } });

    expect((await deactivate).isActive).toBe(false);
  });
});
