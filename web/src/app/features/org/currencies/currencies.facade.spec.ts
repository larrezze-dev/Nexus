import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { CurrenciesFacade } from './currencies.facade';

describe('CurrenciesFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: NxI18nService,
          useValue: { translateErrorCode: (code?: string) => (code ? `translated:${code}` : 'generic') },
        },
      ],
    });

    return {
      facade: TestBed.inject(CurrenciesFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('fetchPage maps the 0-based nx-data-grid page to the contract 1-based page and back', async () => {
    const { facade, httpMock } = setup();

    const result$ = facade.fetchPage(0, 50, null, {});
    const resultPromise = firstValueFrom(result$);

    httpMock.expectOne('/api/platform/v1/currencies?page=1&pageSize=50').flush({
      items: [{ code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive: true }],
      page: 1,
      pageSize: 50,
      total: 1,
    });

    const result = await resultPromise;
    expect(result).toEqual({
      items: [{ code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive: true }],
      page: 0,
      pageSize: 50,
      total: 1,
    });
  });

  it('toggleActive re-reads the ETag then PUTs with If-Match, and returns true on success', async () => {
    const { facade, httpMock } = setup();

    const togglePromise = facade.toggleActive('GBP', false);

    const getReq = httpMock.expectOne('/api/platform/v1/currencies/GBP');
    expect(getReq.request.method).toBe('GET');
    getReq.flush(
      { code: 'GBP', captionI18n: { de: 'Pfund' }, minorUnits: 2, isActive: true },
      { headers: { ETag: '"v1"' } },
    );

    // toggleActive() awaits the GET before issuing the PUT: give the mock's flush() a
    // macrotask boundary to resolve that promise before expecting the second request.
    await new Promise((resolve) => setTimeout(resolve, 0));
    const putReq = httpMock.expectOne('/api/platform/v1/currencies/GBP');
    expect(putReq.request.method).toBe('PUT');
    expect(putReq.request.headers.get('If-Match')).toBe('"v1"');
    expect(putReq.request.body).toEqual({ isActive: false });
    putReq.flush({ code: 'GBP', captionI18n: { de: 'Pfund' }, minorUnits: 2, isActive: false });

    expect(await togglePromise).toBe(true);
    expect(facade.toggleError()).toBeNull();
  });

  it('toggleActive records a readable ORG-R7 error (code + detail) and returns false on 422', async () => {
    const { facade, httpMock } = setup();

    const togglePromise = facade.toggleActive('CHF', false);

    httpMock
      .expectOne('/api/platform/v1/currencies/CHF')
      .flush(
        { code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive: true },
        { headers: { ETag: '"v1"' } },
      );

    await new Promise((resolve) => setTimeout(resolve, 0));
    httpMock.expectOne('/api/platform/v1/currencies/CHF').flush(
      {
        type: 'https://nexus.nussli.ch/problems/unprocessable-entity',
        title: 'La solicitud no se puede procesar',
        status: 422,
        detail: 'CHF está en uso por LE-CH (activa).',
        code: 'ORG-R7',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(await togglePromise).toBe(false);
    expect(facade.toggleError()).toEqual({ code: 'CHF', message: 'CHF está en uso por LE-CH (activa).' });
  });

  it('clearToggleError resets the toggle error', async () => {
    const { facade, httpMock } = setup();

    const togglePromise = facade.toggleActive('CHF', false);
    httpMock
      .expectOne('/api/platform/v1/currencies/CHF')
      .flush({ code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive: true });
    await new Promise((resolve) => setTimeout(resolve, 0));
    httpMock.expectOne('/api/platform/v1/currencies/CHF').flush({}, { status: 500, statusText: 'Server Error' });
    await togglePromise;

    expect(facade.toggleError()).not.toBeNull();
    facade.clearToggleError();
    expect(facade.toggleError()).toBeNull();
  });
});
