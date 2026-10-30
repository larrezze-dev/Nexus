import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Location } from '../../../api/platform/models/location';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { LocationFacade } from './location.facade';

const location: Location = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  code: 'LOC-ZH',
  name: 'Zürich Lagerhalle',
  street: 'Hardturmstrasse 1',
  postalCode: '8005',
  city: 'Zürich',
  region: 'ZH',
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const listItem: LocationListItem = {
  id: location.id,
  code: location.code,
  name: location.name,
  countryCode: location.countryCode,
  isActive: true,
};

describe('LocationFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (code?: string) => code ?? 'generic-error' } },
      ],
    });

    return {
      facade: TestBed.inject(LocationFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('lists locations, converting the 0-based grid page into the 1-based API page', async () => {
    const { facade, httpMock } = setup();

    const result = facade.list(0, 25, null);
    const resultPromise = new Promise((resolve) => result.subscribe(resolve));
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/locations');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush({ items: [listItem], page: 1, pageSize: 25, total: 1 });

    const page = (await resultPromise) as { items: LocationListItem[]; page: number; total: number };
    expect(page.items).toEqual([listItem]);
    expect(page.page).toBe(0);
    expect(page.total).toBe(1);
  });

  it('sends a sort param built from the grid sort model', async () => {
    const { facade, httpMock } = setup();

    const result = facade.list(1, 50, { colId: 'name', direction: 'desc' });
    result.subscribe();
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/locations');
    expect(req.request.params.get('sort')).toBe('-name');
    req.flush({ items: [], page: 2, pageSize: 50, total: 0 });
  });

  it('loads a location by id and captures its ETag', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(location.id);
    const req = httpMock.expectOne(`/api/platform/v1/locations/${location.id}`);
    req.flush(location, { headers: { ETag: '"42"' } });
    await load;

    expect(facade.selected()).toEqual(location);
    expect(facade.loading()).toBe(false);
    expect(facade.error()).toBeNull();
  });

  it('creates a location and exposes it as selected()', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({ name: location.name, countryCode: location.countryCode });
    const req = httpMock.expectOne('/api/platform/v1/locations');
    expect(req.request.method).toBe('POST');
    req.flush(location, { status: 201, statusText: 'Created', headers: { ETag: '"1"' } });
    const created = await create;

    expect(created).toEqual(location);
    expect(facade.selected()).toEqual(location);
  });

  it('updates a location sending the previously captured ETag as If-Match', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(location.id);
    httpMock.expectOne(`/api/platform/v1/locations/${location.id}`).flush(location, { headers: { ETag: '"7"' } });
    await load;

    const update = facade.update(location.id, { name: 'Renamed', countryCode: 'CH' });
    const req = httpMock.expectOne(`/api/platform/v1/locations/${location.id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"7"');
    req.flush({ ...location, name: 'Renamed' }, { headers: { ETag: '"8"' } });
    const updated = await update;

    expect(updated?.name).toBe('Renamed');
  });

  it('exposes an error and leaves selected() untouched when create fails', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({ name: location.name, countryCode: location.countryCode });
    httpMock
      .expectOne('/api/platform/v1/locations')
      .flush({ detail: 'bad input', code: 'BAD_REQUEST' }, { status: 400, statusText: 'Bad Request' });
    const created = await create;

    expect(created).toBeNull();
    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBe('BAD_REQUEST');
  });

  it('reset() clears selected()/error()', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({ name: location.name, countryCode: location.countryCode });
    httpMock.expectOne('/api/platform/v1/locations').flush(location, { status: 201, statusText: 'Created' });
    await create;
    expect(facade.selected()).not.toBeNull();

    facade.reset();

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBeNull();
  });
});
