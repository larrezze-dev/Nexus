import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { Warehouse } from '../../../api/platform/models/warehouse';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { WarehouseFacade } from './warehouse.facade';

const warehouseA = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  locationId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c61',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  businessUnitId: null,
  code: 'WH-A',
  captionI18n: { de: 'A' },
  isActive: true,
} as unknown as Warehouse;
const warehouseB = { ...warehouseA, id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c62', code: 'WH-B' } as Warehouse;

const url = (id: string) => `/api/platform/v1/warehouses/${id}`;
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('WarehouseFacade load() sequencing', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null) => code ?? 'generic' } },
      ],
    });
    return { facade: TestBed.inject(WarehouseFacade), httpMock: TestBed.inject(HttpTestingController) };
  }

  /** Answers the location/legal-entity label lookups `load()` issues after the warehouse GET. */
  async function failLabelLookups(httpMock: HttpTestingController): Promise<void> {
    await tick();
    httpMock
      .match((r) => !r.url.includes('/warehouses/'))
      .forEach((r) => r.flush({}, { status: 404, statusText: 'Not Found' }));
    await tick();
  }

  it('ignores an older load() that resolves after a newer one (selected and ETag stay B)', async () => {
    const { facade, httpMock } = setup();
    const loadA = facade.load(warehouseA.id);
    const loadB = facade.load(warehouseB.id);
    const reqA = httpMock.expectOne(url(warehouseA.id));
    const reqB = httpMock.expectOne(url(warehouseB.id));

    reqB.flush(warehouseB, { headers: { ETag: '"b"' } });
    await failLabelLookups(httpMock);
    await loadB;
    reqA.flush(warehouseA, { headers: { ETag: '"a"' } });
    await tick();
    await loadA;

    expect(facade.selected()?.id).toBe(warehouseB.id);
    expect(facade.loading()).toBe(false);

    const update = facade.update(warehouseB.id, { code: 'X' } as never);
    const put = httpMock.expectOne(url(warehouseB.id));
    expect(put.request.headers.get('If-Match')).toBe('"b"');
    put.flush(warehouseB);
    await update;
  });

  it('discards the result of a load() still in flight when reset() is called', async () => {
    const { facade, httpMock } = setup();
    const load = facade.load(warehouseA.id);
    const req = httpMock.expectOne(url(warehouseA.id));
    facade.reset();
    req.flush(warehouseA, { headers: { ETag: '"a"' } });
    await tick();
    await load;

    expect(facade.selected()).toBeNull();
    expect(facade.loading()).toBe(false);
    expect(httpMock.match(() => true)).toHaveLength(0);
  });
});
