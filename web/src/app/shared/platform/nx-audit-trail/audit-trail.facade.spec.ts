import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { HttpErrorResponse } from '@angular/common/http';
import { Api } from '../../../api/platform/api';
import { getLegalEntity } from '../../../api/platform/fn/legal-entities/get-legal-entity';
import { getLocation } from '../../../api/platform/fn/locations/get-location';
import { listAuditEvents } from '../../../api/platform/fn/audit/list-audit-events';
import { AuditEvent } from '../../../api/platform/models/audit-event';
import { AuditTrailFacade } from './audit-trail.facade';

const LOC = '0192f39e-0000-7000-8000-0000000041aa';
const LE = '0192f390-0000-7000-8000-000000000b12';

function event(version: number, payload: Record<string, unknown> | null = { code: 'A' }): AuditEvent {
  return {
    eventId: `e-${version}`,
    occurredAt: '2026-11-11T13:30:12Z',
    eventType: 'org.warehouse.updated',
    aggregateVersion: version,
    actorUserId: 'u',
    actorDisplayName: 'Anna',
    correlationId: 'c',
    payload,
    previousPayload: null,
  };
}

function setup(invoke: (fn: unknown, params: Record<string, unknown>) => Promise<unknown>) {
  const api = { invoke: vi.fn(invoke) };
  TestBed.configureTestingModule({
    providers: [
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: Api, useValue: api },
      AuditTrailFacade,
    ],
  });
  return { api, facade: TestBed.inject(AuditTrailFacade) };
}

describe('AuditTrailFacade', () => {
  it('loads page 1 and appends page 2 on loadMore', async () => {
    const { api, facade } = setup(async (_fn, params) => ({
      page: params['page'],
      pageSize: 50,
      total: 3,
      items: params['page'] === 1 ? [event(3), event(2)] : [event(1)],
    }));

    await facade.load('warehouse', 'w-1');
    expect(facade.items().map((e) => e.aggregateVersion)).toEqual([3, 2]);
    expect(facade.hasMore()).toBe(true);

    await facade.loadMore();
    expect(facade.items().map((e) => e.aggregateVersion)).toEqual([3, 2, 1]);
    expect(facade.hasMore()).toBe(false);
    expect(api.invoke).toHaveBeenCalledWith(listAuditEvents, {
      aggregateType: 'warehouse',
      aggregateId: 'w-1',
      page: 2,
      pageSize: 50,
    });
  });

  it('exposes an error message and no items when the request fails', async () => {
    const { facade } = setup(async () => {
      throw new HttpErrorResponse({ status: 500, error: { status: 500 } });
    });
    await facade.load('warehouse', 'w-1');
    expect(facade.error()).toBe(true);
    expect(facade.items()).toEqual([]);
    expect(facade.loading()).toBe(false);
  });

  it('resolves references once per id and leaves failures unresolved', async () => {
    const { api, facade } = setup(async (fn, params) => {
      if (fn === listAuditEvents) {
        return {
          page: 1,
          pageSize: 50,
          total: 2,
          items: [
            event(2, { locationId: LOC, legalEntityId: LE }),
            event(1, { locationId: LOC, legalEntityId: LE }),
          ],
        };
      }
      if (fn === getLocation) {
        return { code: 'ZRH', name: 'Zurich' };
      }
      if (fn === getLegalEntity) {
        throw new HttpErrorResponse({ status: 403 });
      }
      throw new Error(`unexpected ${String(params)}`);
    });

    await facade.load('warehouse', 'w-1');
    await vi.waitFor(() => expect(facade.resolvedLabels()[LOC]).toBe('ZRH — Zurich'));

    const calls = api.invoke.mock.calls.map((c) => c[0]);
    expect(calls.filter((fn) => fn === getLocation)).toHaveLength(1);
    expect(calls.filter((fn) => fn === getLegalEntity)).toHaveLength(1);
    expect(facade.resolvedLabels()[LE]).toBeUndefined();

    await facade.reload();
    await Promise.resolve();
    expect(api.invoke.mock.calls.filter((c) => c[0] === getLocation)).toHaveLength(1);
  });

  it('keeps the loaded items visible while reloading', async () => {
    let release: (value: unknown) => void = () => undefined;
    let first = true;
    const { facade } = setup(() => {
      if (first) {
        first = false;
        return Promise.resolve({ page: 1, pageSize: 50, total: 1, items: [event(1)] });
      }
      return new Promise((resolve) => (release = resolve));
    });
    await facade.load('warehouse', 'w-1');
    const pending = facade.reload();
    expect(facade.loading()).toBe(true);
    expect(facade.items()).toHaveLength(1);
    release({ page: 1, pageSize: 50, total: 2, items: [event(2), event(1)] });
    await pending;
    expect(facade.items()).toHaveLength(2);
  });

  it('keeps loaded pages when loadMore fails and retry() retries that page', async () => {
    let failPage2 = true;
    const { api, facade } = setup(async (_fn, params) => {
      if (params['page'] === 2 && failPage2) {
        throw new HttpErrorResponse({ status: 500 });
      }
      return { page: params['page'], pageSize: 50, total: 2, items: [params['page'] === 1 ? event(2) : event(1)] };
    });
    await facade.load('warehouse', 'w-1');
    await facade.loadMore();
    expect(facade.error()).toBe(true);
    expect(facade.items()).toHaveLength(1);

    failPage2 = false;
    await facade.retry();
    expect(facade.error()).toBe(false);
    expect(facade.items().map((e) => e.aggregateVersion)).toEqual([2, 1]);
    expect(api.invoke.mock.calls.map((c) => (c[1] as { page: number }).page)).toEqual([1, 2, 2]);
  });

  it('skips events already present when appending', async () => {
    const { facade } = setup(async (_fn, params) => ({
      page: params['page'],
      pageSize: 50,
      total: 3,
      items: params['page'] === 1 ? [event(3), event(2)] : [event(2), event(1)],
    }));
    await facade.load('warehouse', 'w-1');
    await facade.loadMore();
    expect(facade.items().map((e) => e.aggregateVersion)).toEqual([3, 2, 1]);
  });

  it('discards a stale response that arrives after a newer request', async () => {
    const resolvers: ((value: unknown) => void)[] = [];
    const { facade } = setup(() => new Promise((resolve) => resolvers.push(resolve)));
    const first = facade.load('warehouse', 'w-1');
    const second = facade.reload();
    resolvers[1]({ page: 1, pageSize: 50, total: 1, items: [event(2)] });
    await second;
    resolvers[0]({ page: 1, pageSize: 50, total: 1, items: [event(1)] });
    await first;
    expect(facade.items().map((e) => e.aggregateVersion)).toEqual([2]);
  });

  it('stops offering more when an appended page only holds duplicates (new event arrived between pages)', async () => {
    const full = Array.from({ length: 50 }, (_, i) => event(60 - i));
    const { facade } = setup(async (_fn, params) => ({
      page: params['page'],
      pageSize: 50,
      total: 51,
      items: params['page'] === 1 ? full : [full[49]],
    }));
    await facade.load('warehouse', 'w-1');
    expect(facade.hasMore()).toBe(true);
    await facade.loadMore();
    expect(facade.items()).toHaveLength(50);
    expect(facade.hasMore()).toBe(false);
  });
});
