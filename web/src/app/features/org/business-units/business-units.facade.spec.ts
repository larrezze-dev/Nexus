import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { BusinessUnitsFacade } from './business-units.facade';

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-01',
  captionI18n: { de: 'Vermietung', en: 'Rental' },
  costCenterCode: 'CC-100',
  isActive: true,
};

const listItem: BusinessUnitListItem = {
  id: businessUnit.id,
  legalEntityId: businessUnit.legalEntityId,
  code: businessUnit.code,
  captionI18n: businessUnit.captionI18n,
  isActive: true,
};

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

  it('list() converts the 0-based grid page to the 1-based API page and back', async () => {
    const { facade, httpMock } = setup();

    const result = firstValueFrom(facade.list(0, 25, null));
    const req = httpMock.expectOne(
      (r) => r.url === '/api/platform/v1/business-units' && r.params.get('page') === '1',
    );
    req.flush({ items: [listItem], page: 1, pageSize: 25, total: 1 });

    expect(await result).toEqual({ items: [listItem], page: 0, pageSize: 25, total: 1 });
  });

  it('load() stores the entity and the response ETag', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(businessUnit.id);
    httpMock
      .expectOne(`/api/platform/v1/business-units/${businessUnit.id}`)
      .flush(businessUnit, { headers: { ETag: '"1"' } });
    await load;

    expect(facade.selected()).toEqual(businessUnit);
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
    httpMock
      .expectOne(`/api/platform/v1/business-units/${businessUnit.id}`)
      .flush(businessUnit, { headers: { ETag: '"7"' } });
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
    httpMock
      .expectOne(`/api/platform/v1/business-units/${businessUnit.id}`)
      .flush(businessUnit, { headers: { ETag: '"1"' } });
    await load;

    const deactivate = facade.deactivate(businessUnit.id);
    const req = httpMock.expectOne(`/api/platform/v1/business-units/${businessUnit.id}/deactivate`);
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    req.flush({ ...businessUnit, isActive: false }, { headers: { ETag: '"2"' } });

    expect((await deactivate).isActive).toBe(false);
  });
});
