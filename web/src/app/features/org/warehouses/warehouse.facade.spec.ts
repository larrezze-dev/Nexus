import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
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
const warehouseB = {
  ...warehouseA,
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c62',
  locationId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c63',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c41',
  code: 'WH-B',
} as Warehouse;

const url = (id: string) => `/api/platform/v1/warehouses/${id}`;
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('WarehouseFacade load() sequencing', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null) => code ?? 'generic', activeLang: signal('de') } },
      ],
    });
    return { facade: TestBed.inject(WarehouseFacade), httpMock: TestBed.inject(HttpTestingController) };
  }

  /** Takes the pending location/legal-entity label lookups `load()` issues after a warehouse GET. */
  async function takeLabelLookups(httpMock: HttpTestingController): Promise<TestRequest[]> {
    await tick();
    return httpMock.match((r) => !r.url.includes('/warehouses/'));
  }

  /** Answers label lookups with a body derived from the id, so A's and B's labels differ. */
  function answerLabelLookups(requests: TestRequest[]): void {
    for (const r of requests) {
      const id = r.request.url.split('/').pop() ?? '';
      r.flush({ id, code: `C-${id.slice(-2)}`, name: `N-${id.slice(-2)}`, partyId: id, legalName: `L-${id.slice(-2)}` });
    }
  }

  /** Awaits `promise`, answering follow-up lookups (the legal entity's `Party`) until it settles. */
  async function finish(httpMock: HttpTestingController, promise: Promise<void>): Promise<void> {
    let done = false;
    void promise.then(() => (done = true));
    while (!done) {
      await tick();
      answerLabelLookups(httpMock.match((r) => !r.url.includes('/warehouses/')));
    }
  }

  it('ignores an older load() that resolves after a newer one (selected and ETag stay B)', async () => {
    const { facade, httpMock } = setup();
    const loadA = facade.load(warehouseA.id);
    const loadB = facade.load(warehouseB.id);
    const reqA = httpMock.expectOne(url(warehouseA.id));
    const reqB = httpMock.expectOne(url(warehouseB.id));

    reqB.flush(warehouseB, { headers: { ETag: '"b"' } });
    answerLabelLookups(await takeLabelLookups(httpMock));
    await finish(httpMock, loadB);
    reqA.flush(warehouseA, { headers: { ETag: '"a"' } });
    answerLabelLookups(await takeLabelLookups(httpMock));
    await finish(httpMock, loadA);

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
    answerLabelLookups(await takeLabelLookups(httpMock));
    await finish(httpMock, load);

    expect(facade.selected()).toBeNull();
    expect(facade.loading()).toBe(false);
    expect(httpMock.match(() => true)).toHaveLength(0);
  });

  it('ignores a late failure of an older load()', async () => {
    const { facade, httpMock } = setup();
    const loadA = facade.load(warehouseA.id);
    const loadB = facade.load(warehouseB.id);
    const reqA = httpMock.expectOne(url(warehouseA.id));
    httpMock.expectOne(url(warehouseB.id)).flush(warehouseB, { headers: { ETag: '"b"' } });
    answerLabelLookups(await takeLabelLookups(httpMock));
    await finish(httpMock, loadB);
    reqA.flush({}, { status: 500, statusText: 'err' });
    await finish(httpMock, loadA);

    expect(facade.selected()?.id).toBe(warehouseB.id);
    expect(facade.error()).toBeNull();
  });

  it('does not apply A\'s resolved labels when a newer load() started while they were in flight', async () => {
    const { facade, httpMock } = setup();
    const loadA = facade.load(warehouseA.id);
    httpMock.expectOne(url(warehouseA.id)).flush(warehouseA, { headers: { ETag: '"a"' } });
    const labelLookupsA = await takeLabelLookups(httpMock);
    expect(labelLookupsA.length).toBeGreaterThan(0);

    const loadB = facade.load(warehouseB.id); // B starts before A's label lookups resolve
    answerLabelLookups(labelLookupsA);
    await finish(httpMock, loadA);

    expect(facade.resolvedLocationLabel()).toBeNull();
    expect(facade.resolvedLegalEntityLabel()).toBeNull();
    expect(facade.resolvedBusinessUnitLabel()).toBeNull();

    httpMock.expectOne(url(warehouseB.id)).flush(warehouseB, { headers: { ETag: '"b"' } });
    answerLabelLookups(await takeLabelLookups(httpMock));
    await finish(httpMock, loadB);
    expect(facade.resolvedLocationLabel()).toBe('C-63 — N-63');
  });

  it('does not apply resolved labels when reset() is called while they were in flight', async () => {
    const { facade, httpMock } = setup();
    const load = facade.load(warehouseA.id);
    httpMock.expectOne(url(warehouseA.id)).flush(warehouseA, { headers: { ETag: '"a"' } });
    const labelLookups = await takeLabelLookups(httpMock);
    facade.reset();
    answerLabelLookups(labelLookups);
    await finish(httpMock, load);

    expect(facade.resolvedLocationLabel()).toBeNull();
    expect(facade.resolvedLegalEntityLabel()).toBeNull();
  });
});
