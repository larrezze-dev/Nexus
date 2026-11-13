import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { WarehouseFacade } from './warehouse.facade';
import { WarehouseFormValue, WarehouseSaveError } from './warehouse.models';

const base = '/api/platform/v1';
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

const newLocation = { code: '', name: 'Depot', street: 'Main 1', postalCode: '8000', city: 'Zürich', region: '', countryCode: 'CH' };
const value = (over: Partial<WarehouseFormValue>): WarehouseFormValue => ({
  code: 'WH-1',
  captionI18n: { de: 'Lager' },
  legalEntityId: 'le-1',
  businessUnitId: null,
  addressMode: 'existingLocation',
  locationId: 'loc-1',
  newLocation,
  ...over,
});
const warehouse = { id: 'w-1', code: 'WH-1', captionI18n: { de: 'Lager', es: 'Almacén' }, locationId: 'loc-1', legalEntityId: 'le-1', isActive: true };

describe('WarehouseFacade save()', () => {
  const activeLang = signal('de');
  function setup() {
    activeLang.set('de');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (c: string | null) => c ?? 'generic', activeLang } },
      ],
    });
    return { facade: TestBed.inject(WarehouseFacade), http: TestBed.inject(HttpTestingController) };
  }

  it('existing location: creates the warehouse with that locationId and never calls /locations', async () => {
    const { facade, http } = setup();
    const result = facade.save(null, value({ locationId: 'loc-7', businessUnitId: 'bu-1' }));
    const req = http.expectOne(`${base}/warehouses`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({ locationId: 'loc-7', legalEntityId: 'le-1', businessUnitId: 'bu-1', code: 'WH-1' });
    req.flush(warehouse);
    await expect(result).resolves.toMatchObject({ id: 'w-1' });
    http.expectNone(`${base}/locations`);
  });

  it('existing location on edit: PUT with If-Match from the loaded ETag and no legalEntityId', async () => {
    const { facade, http } = setup();
    const load = facade.load('w-1');
    http.expectOne(`${base}/warehouses/w-1`).flush(warehouse, { headers: { ETag: '"v1"' } });
    await tick();
    for (const r of http.match((r) => !r.url.includes('/warehouses/'))) {
      r.flush({ id: 'x', code: 'C', name: 'N', partyId: 'p', legalName: 'L', captionI18n: { de: 'B' } });
      await tick();
    }
    for (const r of http.match((r) => !r.url.includes('/warehouses/'))) {
      r.flush({ id: 'p', legalName: 'L' });
    }
    await load;

    const result = facade.save('w-1', value({ locationId: 'loc-2' }));
    const req = http.expectOne(`${base}/warehouses/w-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    expect(req.request.body).not.toHaveProperty('legalEntityId');
    expect(req.request.body.locationId).toBe('loc-2');
    req.flush(warehouse, { headers: { ETag: '"v2"' } });
    await result;
  });

  it('new address: POST /locations first, then the warehouse with the returned id', async () => {
    const { facade, http } = setup();
    const result = facade.save(null, value({ addressMode: 'newLocation', locationId: null }));
    expect(facade.saveStep()).toBe('creatingLocation');
    const loc = http.expectOne(`${base}/locations`);
    expect(loc.request.body).toEqual({
      code: null,
      name: 'Depot',
      street: 'Main 1',
      postalCode: '8000',
      city: 'Zürich',
      region: null,
      countryCode: 'CH',
    });
    loc.flush({ id: 'loc-new', name: 'Depot', code: null }, { headers: { ETag: '"l1"' } });
    await tick();
    expect(facade.saveStep()).toBe('savingWarehouse');
    const wh = http.expectOne(`${base}/warehouses`);
    expect(wh.request.body.locationId).toBe('loc-new');
    wh.flush({ ...warehouse, locationId: 'loc-new' });
    await result;
    expect(facade.saveStep()).toBe('idle');
    expect(facade.pendingLocation()).toBeNull();
  });

  it('second step fails: reports the created location and a retry reuses it without a second POST /locations', async () => {
    const { facade, http } = setup();
    const first = facade.save(null, value({ addressMode: 'newLocation', locationId: null }));
    http.expectOne(`${base}/locations`).flush({ id: 'loc-new', name: 'Depot', code: null }, { headers: { ETag: '"l1"' } });
    await tick();
    http.expectOne(`${base}/warehouses`).flush({ code: 'ORG-R3' }, { status: 422, statusText: 'Unprocessable' });

    const error = (await first.catch((e: unknown) => e)) as WarehouseSaveError;
    expect(error).toBeInstanceOf(WarehouseSaveError);
    expect(error.step).toBe('warehouse');
    expect(error.locationCreated).toBe(true);
    expect(error.locationId).toBe('loc-new');
    expect(facade.describeError(error.cause).code).toBe('ORG-R3');
    expect(facade.pendingLocation()).toMatchObject({ id: 'loc-new', name: 'Depot' });
    expect(facade.saving()).toBe(false);

    const retry = facade.save(null, value({ addressMode: 'newLocation', locationId: null }));
    await tick();
    http.expectNone(`${base}/locations`);
    const wh = http.expectOne(`${base}/warehouses`);
    expect(wh.request.body.locationId).toBe('loc-new');
    wh.flush(warehouse);
    await retry;
    expect(facade.pendingLocation()).toBeNull();
  });

  it('retry after editing the address updates the created location (If-Match) instead of creating another', async () => {
    const { facade, http } = setup();
    const first = facade.save(null, value({ addressMode: 'newLocation', locationId: null }));
    http.expectOne(`${base}/locations`).flush({ id: 'loc-new', name: 'Depot', code: null }, { headers: { ETag: '"l1"' } });
    await tick();
    http.expectOne(`${base}/warehouses`).flush({}, { status: 500, statusText: 'err' });
    await first.catch(() => undefined);

    const retry = facade.save(
      null,
      value({ addressMode: 'newLocation', locationId: null, newLocation: { ...newLocation, name: 'Depot 2' } }),
    );
    const put = http.expectOne(`${base}/locations/loc-new`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('If-Match')).toBe('"l1"');
    put.flush({ id: 'loc-new', name: 'Depot 2', code: null }, { headers: { ETag: '"l2"' } });
    await tick();
    http.expectNone(`${base}/locations`);
    http.expectOne(`${base}/warehouses`).flush(warehouse);
    await retry;
  });

  it('first step fails: no warehouse call, error says nothing was created', async () => {
    const { facade, http } = setup();
    const result = facade.save(null, value({ addressMode: 'newLocation', locationId: null }));
    http.expectOne(`${base}/locations`).flush({}, { status: 400, statusText: 'bad' });
    const error = (await result.catch((e: unknown) => e)) as WarehouseSaveError;
    expect(error.step).toBe('location');
    expect(error.locationCreated).toBe(false);
    http.expectNone(`${base}/warehouses`);
    expect(facade.pendingLocation()).toBeNull();
  });

  it('"same as legal entity" is refused without any request', async () => {
    const { facade, http } = setup();
    await expect(facade.save(null, value({ addressMode: 'sameAsLegalEntity' }))).rejects.toThrow(/not available/);
    http.expectNone(() => true);
  });

  it('deactivate propagates the HTTP error', async () => {
    const { facade, http } = setup();
    const result = facade.deactivate('w-1');
    http.expectOne(`${base}/warehouses/w-1/deactivate`).flush({ code: 'X' }, { status: 422, statusText: 'x' });
    await expect(result).rejects.toBeDefined();
    expect(facade.saving()).toBe(false);
  });

  it('title follows the active language (breadcrumb)', async () => {
    const { facade, http } = setup();
    const load = facade.load('w-1');
    http.expectOne(`${base}/warehouses/w-1`).flush(warehouse);
    await tick();
    expect(facade.title()).toBe('Lager');
    activeLang.set('es');
    expect(facade.title()).toBe('Almacén');
    http.match(() => true).forEach((r) => r.flush({}, { status: 404, statusText: 'nf' }));
    await load;
  });

  it('load failure clears selected and sets error (page shows Retry)', async () => {
    const { facade, http } = setup();
    const load = facade.load('w-1');
    http.expectOne(`${base}/warehouses/w-1`).flush({ code: 'X' }, { status: 500, statusText: 'x' });
    await load;
    expect(facade.selected()).toBeNull();
    expect(facade.error()).not.toBeNull();
    expect(facade.loading()).toBe(false);
  });
});
