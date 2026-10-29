import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { OrgTreeFacade } from './org-tree.facade';

describe('OrgTreeFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: NxI18nService,
          useValue: { getActiveLanguage: () => 'en', translateErrorCode: () => 'error' },
        },
      ],
    });

    return {
      facade: TestBed.inject(OrgTreeFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('assembles the tree from exactly 4 list calls (one per level), nesting warehouses under their BU or directly under their LE', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();

    httpMock.expectOne('/api/platform/v1/groups?page=1&pageSize=200').flush({
      items: [{ id: 'g1', partyId: 'p-g1', code: 'NUSSLI', reportingCurrencyCode: 'CHF' }],
      page: 1,
      pageSize: 200,
      total: 1,
    });
    httpMock.expectOne('/api/platform/v1/legal-entities?page=1&pageSize=200').flush({
      items: [
        { id: 'le1', groupId: 'g1', code: 'LE-CH', currencyCode: 'CHF', countryCode: 'CH', isActive: true },
      ],
      page: 1,
      pageSize: 200,
      total: 1,
    });
    httpMock.expectOne('/api/platform/v1/business-units?page=1&pageSize=200').flush({
      items: [
        {
          id: 'bu1',
          legalEntityId: 'le1',
          code: 'BU-01',
          captionI18n: { de: 'Vermietung', en: 'Rental' },
          isActive: true,
        },
      ],
      page: 1,
      pageSize: 200,
      total: 1,
    });
    httpMock.expectOne('/api/platform/v1/warehouses?page=1&pageSize=200').flush({
      items: [
        {
          id: 'wh1',
          legalEntityId: 'le1',
          businessUnitId: 'bu1',
          code: 'WH-01',
          captionI18n: { de: 'Lager' },
          isActive: true,
        },
        {
          id: 'wh2',
          legalEntityId: 'le1',
          businessUnitId: null,
          code: 'WH-DIRECT',
          captionI18n: { de: 'Direkt' },
          isActive: false,
        },
      ],
      page: 1,
      pageSize: 200,
      total: 2,
    });

    await load;
    httpMock.verify();

    expect(facade.loading()).toBe(false);
    expect(facade.error()).toBeNull();

    const [group] = facade.nodes();
    expect(group.kind).toBe('group');
    expect(group.label).toBe('NUSSLI');
    expect(group.isActive).toBeNull();

    const [legalEntity] = group.children;
    expect(legalEntity.kind).toBe('legalEntity');
    expect(legalEntity.label).toBe('LE-CH');
    expect(legalEntity.isActive).toBe(true);
    expect(legalEntity.children).toHaveLength(2);

    const businessUnit = legalEntity.children.find((node) => node.kind === 'businessUnit');
    expect(businessUnit?.label).toBe('Rental');
    expect(businessUnit?.children).toHaveLength(1);
    expect(businessUnit?.children[0]).toMatchObject({ kind: 'warehouse', id: 'wh1', isActive: true });

    const directWarehouse = legalEntity.children.find((node) => node.kind === 'warehouse');
    expect(directWarehouse).toMatchObject({ id: 'wh2', isActive: false, label: 'Direkt' });
  });

  it('paginates a level that reports more rows than one page, but still issues exactly one request for the other three levels', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();

    const groupsPage1 = httpMock.expectOne('/api/platform/v1/groups?page=1&pageSize=200');
    groupsPage1.flush({
      items: Array.from({ length: 200 }, (_, i) => ({
        id: `g${i}`,
        partyId: `p${i}`,
        code: `G${i}`,
        reportingCurrencyCode: 'CHF',
      })),
      page: 1,
      pageSize: 200,
      total: 201,
    });
    // fetchAllPages() awaits the page-1 response before requesting page 2: the mock's
    // synchronous flush() resolves that promise on the microtask queue, so a macrotask
    // boundary is needed before the second request exists to expect.
    await new Promise((resolve) => setTimeout(resolve, 0));
    const groupsPage2 = httpMock.expectOne('/api/platform/v1/groups?page=2&pageSize=200');
    groupsPage2.flush({
      items: [{ id: 'g200', partyId: 'p200', code: 'G200', reportingCurrencyCode: 'CHF' }],
      page: 2,
      pageSize: 200,
      total: 201,
    });

    httpMock
      .expectOne('/api/platform/v1/legal-entities?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });
    httpMock
      .expectOne('/api/platform/v1/business-units?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });
    httpMock
      .expectOne('/api/platform/v1/warehouses?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });

    await load;
    httpMock.verify();

    expect(facade.nodes()).toHaveLength(201);
  });

  it('exposes a readable error and an empty tree when a level fails to load', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load();

    httpMock
      .expectOne('/api/platform/v1/groups?page=1&pageSize=200')
      .flush({ detail: 'boom' }, { status: 500, statusText: 'Server Error' });
    httpMock
      .expectOne('/api/platform/v1/legal-entities?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });
    httpMock
      .expectOne('/api/platform/v1/business-units?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });
    httpMock
      .expectOne('/api/platform/v1/warehouses?page=1&pageSize=200')
      .flush({ items: [], page: 1, pageSize: 200, total: 0 });

    await load;

    expect(facade.loading()).toBe(false);
    expect(facade.nodes()).toEqual([]);
    expect(facade.error()).toBe('boom');
  });
});
