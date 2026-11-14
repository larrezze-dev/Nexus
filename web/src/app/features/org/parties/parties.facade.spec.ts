import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import type { Party } from '../../../api/platform/models/party';
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { PartiesFacade } from './parties.facade';

const party: Party = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c20',
  partyType: 'ORGANIZATION',
  legalName: 'Acme Events SA',
  displayName: 'Acme Events',
  taxId: null,
  erpCompanyCode: null,
  isInternal: false,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

describe('PartiesFacade', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null) => code ?? 'generic' } },
      ],
    });

    return {
      facade: TestBed.inject(PartiesFacade),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('list() forwards q/partyType/isInternal and maps the page to the 0-based grid envelope', async () => {
    const { facade, httpMock } = setup();

    const items: PartyListItem[] = [
      { id: party.id, partyType: 'ORGANIZATION', legalName: party.legalName, displayName: party.displayName, isInternal: false, isActive: true },
    ];

    const result = firstValueFrom(
      facade.list(0, 25, null, { q: 'acme', partyType: 'ORGANIZATION', isInternal: false }),
    );
    const req = httpMock.expectOne((r) => r.url === '/api/platform/v1/parties');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('q')).toBe('acme');
    expect(req.request.params.get('partyType')).toBe('ORGANIZATION');
    expect(req.request.params.get('isInternal')).toBe('false');
    req.flush({ items, page: 1, pageSize: 25, total: 1 });

    const page = await result;
    expect(page).toEqual({ items, page: 0, pageSize: 25, total: 1 });
  });

  it('create() posts a CreatePartyRequest and stores the result + ETag', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({
      partyType: 'ORGANIZATION',
      legalName: 'Acme Events SA',
      displayName: 'Acme Events',
      isInternal: false,
    });
    const req = httpMock.expectOne('/api/platform/v1/parties');
    expect(req.request.method).toBe('POST');
    req.flush(party, { headers: { ETag: '"v1"' } });

    const result = await create;
    expect(result).toEqual(party);
  });

  it('update() sends the ETag captured by load() as If-Match', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(party.id);
    httpMock.expectOne(`/api/platform/v1/parties/${party.id}`).flush(party, { headers: { ETag: '"v1"' } });
    await load;

    const update = facade.update(party.id, {
      legalName: 'Acme Events SA (updated)',
      displayName: party.displayName,
      taxId: null,
      erpCompanyCode: null,
    });
    const req = httpMock.expectOne(`/api/platform/v1/parties/${party.id}`);
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    req.flush({ ...party, legalName: 'Acme Events SA (updated)' });

    const result = await update;
    expect(result?.legalName).toBe('Acme Events SA (updated)');
  });

  it('deactivate() returns the deactivated party on success', async () => {
    const { facade, httpMock } = setup();

    const deactivate = facade.deactivate(party.id);
    httpMock.expectOne(`/api/platform/v1/parties/${party.id}/deactivate`).flush({ ...party, isActive: false });

    const result = await deactivate;
    expect(result?.isActive).toBe(false);
  });

  it('create() lets the HTTP error reach the caller (the page maps 400/422)', async () => {
    const { facade, httpMock } = setup();

    const create = facade.create({ partyType: 'ORGANIZATION', legalName: '' });
    httpMock
      .expectOne('/api/platform/v1/parties')
      .flush({ status: 400, errors: { legalName: ['Required'] } }, { status: 400, statusText: 'Bad Request' });

    await expect(create).rejects.toMatchObject({ status: 400 });
  });

  it('load() clears the record and sets error when the GET fails (retry state)', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(party.id);
    httpMock.expectOne(`/api/platform/v1/parties/${party.id}`).flush(null, { status: 500, statusText: 'Server Error' });
    await load;

    expect(facade.selected()).toBeNull();
    expect(facade.error()).toBeTruthy();
  });

  it('load(): a late response of an older load never overwrites the newer one', async () => {
    const { facade, httpMock } = setup();
    const older = { ...party, id: 'p-old', legalName: 'Old AG' };
    const newer = { ...party, id: 'p-new', legalName: 'New AG' };

    const first = facade.load(older.id);
    const second = facade.load(newer.id);
    httpMock.expectOne('/api/platform/v1/parties/p-new').flush(newer, { headers: { ETag: '"new"' } });
    await second;
    httpMock.expectOne('/api/platform/v1/parties/p-old').flush(older, { headers: { ETag: '"old"' } });
    await first;

    expect(facade.selected()?.legalName).toBe('New AG');
    expect(facade.loading()).toBe(false);
  });

  it('a late failure of an older load does not flag the newer one as failed', async () => {
    const { facade, httpMock } = setup();
    const newer = { ...party, id: 'p-new', legalName: 'New AG' };

    const first = facade.load('p-old');
    const second = facade.load(newer.id);
    httpMock.expectOne('/api/platform/v1/parties/p-new').flush(newer);
    await second;
    httpMock
      .expectOne('/api/platform/v1/parties/p-old')
      .flush({}, { status: 500, statusText: 'Server Error' });
    await first;

    expect(facade.selected()?.legalName).toBe('New AG');
    expect(facade.error()).toBeNull();
  });

  it('reset() invalidates a load in flight: its response is ignored and loading() is false', async () => {
    const { facade, httpMock } = setup();

    const load = facade.load(party.id);
    facade.reset();
    expect(facade.loading()).toBe(false);
    httpMock.expectOne(`/api/platform/v1/parties/${party.id}`).flush(party);
    await load;

    expect(facade.selected()).toBeNull();
  });
});
