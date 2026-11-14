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
          useValue: {
            translateErrorCode: (code?: string) => (code ? `translated:${code}` : 'generic'),
          },
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

  const chf = { code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2, isActive: true };

  it('toggleActive PUTs with the ETag of the load (get), without re-reading it', async () => {
    const { facade, httpMock } = setup();

    const loadPromise = facade.get('GBP');
    httpMock
      .expectOne('/api/platform/v1/currencies/GBP')
      .flush({ ...chf, code: 'GBP' }, { headers: { ETag: '"v1"' } });
    await loadPromise;

    const togglePromise = facade.toggleActive('GBP', false);
    // No second GET: the PUT is the next and only request.
    const putReq = httpMock.expectOne('/api/platform/v1/currencies/GBP');
    expect(putReq.request.method).toBe('PUT');
    expect(putReq.request.headers.get('If-Match')).toBe('"v1"');
    expect(putReq.request.body).toEqual({ isActive: false });
    putReq.flush({ ...chf, code: 'GBP', isActive: false }, { headers: { ETag: '"v2"' } });

    expect(await togglePromise).toBe(true);
    expect(facade.toggleError()).toBeNull();
    // The new ETag is kept for the next write.
    expect(facade.etag('GBP')).toBe('"v2"');
  });

  it('toggleActive reads the currency once only when it was never loaded', async () => {
    const { facade, httpMock } = setup();

    const togglePromise = facade.toggleActive('GBP', false);
    const getReq = httpMock.expectOne('/api/platform/v1/currencies/GBP');
    expect(getReq.request.method).toBe('GET');
    getReq.flush({ ...chf, code: 'GBP' }, { headers: { ETag: '"v1"' } });
    await new Promise((resolve) => setTimeout(resolve, 0));
    const putReq = httpMock.expectOne('/api/platform/v1/currencies/GBP');
    expect(putReq.request.headers.get('If-Match')).toBe('"v1"');
    putReq.flush({ ...chf, code: 'GBP', isActive: false });

    expect(await togglePromise).toBe(true);
  });

  it('toggleActive records the translated ORG-R7 error (never the English detail) on 422', async () => {
    const { facade, httpMock } = setup();

    const loadPromise = facade.get('CHF');
    httpMock.expectOne('/api/platform/v1/currencies/CHF').flush(chf, { headers: { ETag: '"v1"' } });
    await loadPromise;

    const togglePromise = facade.toggleActive('CHF', false);
    httpMock.expectOne('/api/platform/v1/currencies/CHF').flush(
      {
        type: 'https://nexus.nussli.ch/problems/unprocessable-entity',
        title: 'The request cannot be processed',
        status: 422,
        detail: 'CHF is used by LE-CH (active).',
        code: 'ORG-R7',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );

    expect(await togglePromise).toBe(false);
    expect(facade.toggleError()).toEqual({
      code: 'CHF',
      message: 'translated:ORG-R7',
      problemCode: 'ORG-R7',
    });
  });

  it('create POSTs the new currency to /v1/currencies', async () => {
    const { facade, httpMock } = setup();

    const promise = facade.create({ code: 'SEK', captionI18n: { de: 'Schwedische Krone' }, minorUnits: 2 });
    const req = httpMock.expectOne('/api/platform/v1/currencies');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      code: 'SEK',
      captionI18n: { de: 'Schwedische Krone' },
      minorUnits: 2,
    });
    req.flush({ code: 'SEK', captionI18n: { de: 'Schwedische Krone' }, minorUnits: 2, isActive: true });

    expect((await promise).code).toBe('SEK');
  });

  it('create lets a 409 CONFLICT propagate so the page can mark the code field', async () => {
    const { facade, httpMock } = setup();

    const promise = facade.create({ code: 'CHF', captionI18n: { de: 'Franken' }, minorUnits: 2 });
    httpMock.expectOne('/api/platform/v1/currencies').flush(
      { type: 'https://nexus.nussli.ch/problems/conflict', code: 'CONFLICT', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );

    await expect(promise).rejects.toMatchObject({ status: 409, error: { code: 'CONFLICT' } });
  });

  it('rename PUTs the current isActive plus the new captionI18n with the ETag of the load', async () => {
    const { facade, httpMock } = setup();

    const loadPromise = facade.get('CHF');
    httpMock
      .expectOne('/api/platform/v1/currencies/CHF')
      .flush({ ...chf, isActive: false }, { headers: { ETag: '"v7"' } });
    await loadPromise;

    const promise = facade.rename('CHF', { de: 'Schweizer Franken', en: 'Swiss franc' }, facade.etag('CHF'));
    const req = httpMock.expectOne('/api/platform/v1/currencies/CHF');
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"v7"');
    expect(req.request.body).toEqual({
      isActive: false,
      captionI18n: { de: 'Schweizer Franken', en: 'Swiss franc' },
    });
    req.flush(
      { ...chf, isActive: false, captionI18n: { de: 'Schweizer Franken', en: 'Swiss franc' } },
      { headers: { ETag: '"v8"' } },
    );

    await promise;
    expect(facade.etag('CHF')).toBe('"v8"');
  });

  it('get reads one currency for the detail page', async () => {
    const { facade, httpMock } = setup();

    const promise = facade.get('EUR');
    const req = httpMock.expectOne('/api/platform/v1/currencies/EUR');
    expect(req.request.method).toBe('GET');
    req.flush({ code: 'EUR', captionI18n: { de: 'Euro' }, minorUnits: 2, isActive: true });

    expect(await promise).toEqual({
      code: 'EUR',
      captionI18n: { de: 'Euro' },
      minorUnits: 2,
      isActive: true,
    });
  });

  it('clearToggleError resets the toggle error', async () => {
    const { facade, httpMock } = setup();

    const loadPromise = facade.get('CHF');
    httpMock.expectOne('/api/platform/v1/currencies/CHF').flush(chf, { headers: { ETag: '"v1"' } });
    await loadPromise;

    const togglePromise = facade.toggleActive('CHF', false);
    httpMock
      .expectOne('/api/platform/v1/currencies/CHF')
      .flush({}, { status: 500, statusText: 'Server Error' });
    await togglePromise;

    expect(facade.toggleError()).not.toBeNull();
    facade.clearToggleError();
    expect(facade.toggleError()).toBeNull();
  });
});
