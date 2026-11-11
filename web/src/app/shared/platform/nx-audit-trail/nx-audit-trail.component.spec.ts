import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoLoader, TranslocoService, TranslocoTestingModule, provideTransloco } from '@jsverse/transloco';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';
import { AuditEvent } from '../../../api/platform/models/audit-event';
import { AuditTrailFacade } from './audit-trail.facade';
import { NxAuditTrailComponent } from './nx-audit-trail.component';

const EN = {
  common: { yes: 'Yes', no: 'No', reload: 'Reload' },
  audit: {
    summary: '{{count}} events',
    summaryOne: '1 event',
    loading: 'Loading audit trail',
    noMore: 'No more events',
    previousUnavailable: 'Previous version unavailable',
    refresh: 'Refresh',
    loadMore: 'Load more',
    empty: { title: 'No events yet', hint: 'hint' },
    error: 'Could not load',
    retry: 'Retry',
    pii: 'Personal data notice',
    unknownActor: 'Unknown user',
    technicalDetails: 'Technical details',
    field: 'Field',
    before: 'Before',
    after: 'After',
    value: 'Value',
    initialValues: 'Initial values',
    emptyValue: '(empty)',
    fields: { code: 'Code', captionI18n: 'Name', businessUnitId: 'Business unit', isActive: 'Active' },
  },
  events: { org: { warehouse: { created: 'Warehouse created', updated: 'Warehouse updated' } } },
};

const BU = '0192f3a1-7e3b-7c5a-9c2e-6a2f8e9b0c7c';

function ev(version: number, over: Partial<AuditEvent> = {}): AuditEvent {
  return {
    eventId: `ev-${version}`,
    occurredAt: '2026-11-11T13:32:05Z',
    eventType: 'org.warehouse.updated',
    aggregateVersion: version,
    actorUserId: 'u',
    actorDisplayName: 'Anna Keller',
    correlationId: 'corr',
    payload: {},
    previousPayload: {},
    ...over,
  };
}

function setup(
  items: AuditEvent[],
  extra: { loading?: boolean; error?: boolean; hasMore?: boolean; total?: number; labels?: Record<string, string> } = {},
) {
  const facade = {
    items: signal(items),
    total: signal(extra.total ?? items.length),
    loading: signal(extra.loading ?? false),
    error: signal(extra.error ?? false),
    hasMore: signal(extra.hasMore ?? false),
    resolvedLabels: signal<Record<string, string>>(extra.labels ?? {}),
    retry: vi.fn().mockResolvedValue(undefined),
    load: vi.fn().mockResolvedValue(undefined),
    reload: vi.fn().mockResolvedValue(undefined),
    loadMore: vi.fn().mockResolvedValue(undefined),
  };
  TestBed.configureTestingModule({
    imports: [
      NxAuditTrailComponent,
      TranslocoTestingModule.forRoot({
        langs: { en: EN },
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      }),
    ],
    providers: [provideNoopAnimations()],
  });
  TestBed.overrideComponent(NxAuditTrailComponent, { set: { providers: [{ provide: AuditTrailFacade, useValue: facade }] } });
  const fixture = TestBed.createComponent(NxAuditTrailComponent);
  fixture.componentRef.setInput('aggregateType', 'warehouse');
  fixture.componentRef.setInput('aggregateId', 'w-1');
  fixture.detectChanges();
  return { fixture, facade, root: fixture.nativeElement as HTMLElement };
}

const q = (root: HTMLElement, id: string) => root.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`);

describe('NxAuditTrailComponent', () => {
  it('loads on init and renders three events newest-first with diff rows', () => {
    const { facade, root } = setup([
      ev(3, { payload: { code: 'B', captionI18n: { de: 'Neu' } }, previousPayload: { code: 'B', captionI18n: { de: 'Alt' } } }),
      ev(2, { payload: { businessUnitId: BU }, previousPayload: { businessUnitId: null } }),
      ev(1, {
        eventType: 'org.warehouse.created',
        payload: { id: 'x', code: 'A', isActive: true },
        previousPayload: null,
      }),
    ]);

    expect(facade.load).toHaveBeenCalledWith('warehouse', 'w-1');
    expect([...q(root, 'audit-version')].map((e) => e.textContent?.trim())).toEqual(['v3', 'v2', 'v1']);
    expect([...q(root, 'audit-title')].map((e) => e.textContent?.trim())).toEqual([
      'Warehouse updated',
      'Warehouse updated',
      'Warehouse created',
    ]);

    const changes = [...q(root, 'audit-changes')];
    expect(changes).toHaveLength(2);
    expect(changes[0].querySelectorAll('tbody tr')).toHaveLength(1);
    expect(changes[0].textContent).toContain('Name (de)');
    expect(changes[0].textContent).toContain('Alt');
    expect(changes[0].textContent).toContain('Neu');
    // null -> value, with a short uuid and the full uuid as title
    expect(changes[1].textContent).toContain('(empty)');
    expect(changes[1].querySelector(`[title="${BU}"]`)?.textContent).toBe('0192f3a1…0c7c');

    const initial = q(root, 'audit-initial')[0];
    expect(initial.textContent).toContain('Yes');
    expect(initial.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(root.querySelectorAll('th[scope="col"]')).toHaveLength(8);
    expect(root.querySelectorAll('th[scope="row"]')).toHaveLength(4);
  });

  it('shows the empty state when there are no events', () => {
    const { root } = setup([]);
    expect(q(root, 'audit-empty')[0].textContent).toContain('No events yet');
    expect(q(root, 'audit-list')).toHaveLength(0);
  });

  it('shows the PII notice instead of a diff', () => {
    const { root } = setup([ev(1, { payload: null, previousPayload: null })]);
    expect(q(root, 'audit-pii')[0].textContent).toContain('Personal data notice');
    expect(q(root, 'audit-changes')).toHaveLength(0);
  });

  it('shows an italic unknown actor when actorDisplayName is null', () => {
    const { root } = setup([ev(1, { actorDisplayName: null })]);
    const actor = q(root, 'audit-actor')[0];
    expect(actor.tagName).toBe('EM');
    expect(actor.textContent).toBe('Unknown user');
  });

  it('shows an alert with retry when loading failed and reloads on click', () => {
    const { root, facade } = setup([], { error: true });
    expect(q(root, 'audit-error')[0].getAttribute('role')).toBe('alert');
    q(root, 'audit-retry')[0].click();
    expect(facade.retry).toHaveBeenCalled();
  });

  it('shows the time in Europe/Zurich', () => {
    const { root } = setup([ev(1, { occurredAt: '2026-11-11T13:32:05Z' })]);
    expect(root.querySelector('.nx-audit__meta')?.textContent).toContain('14:32:05');
  });

  it('renders a resolved reference label with the full uuid as title', () => {
    const { root } = setup(
      [ev(1, { payload: { businessUnitId: BU }, previousPayload: { businessUnitId: null } })],
      { labels: { [BU]: 'BU-01' } },
    );
    const cell = root.querySelector(`[title="${BU}"]`) as HTMLElement;
    expect(cell.textContent).toBe('BU-01');
    expect(cell.classList.contains('nx-mono')).toBe(false);
  });

  it('wires the refresh and load more buttons', () => {
    const { root, facade } = setup([ev(1)], { hasMore: true, total: 120 });
    q(root, 'audit-refresh')[0].click();
    q(root, 'audit-load-more')[0].click();
    expect(facade.reload).toHaveBeenCalledTimes(1);
    expect(facade.loadMore).toHaveBeenCalledTimes(1);
    expect(q(root, 'audit-no-more')).toHaveLength(0);
    expect(q(root, 'audit-summary')[0].textContent).toContain('120 events');
  });

  it('shows the skeleton only on the first load, with a labelled progress bar', () => {
    const first = setup([], { loading: true });
    expect(q(first.root, 'audit-skeleton')).toHaveLength(1);
    expect(q(first.root, 'audit-empty')).toHaveLength(0);
    expect(first.root.querySelector('mat-progress-bar')?.getAttribute('aria-label')).toBe('Loading audit trail');
  });

  it('keeps the list and shows no skeleton while reloading', () => {
    const { root } = setup([ev(1)], { loading: true });
    expect(q(root, 'audit-skeleton')).toHaveLength(0);
    expect(q(root, 'audit-event')).toHaveLength(1);
    expect(q(root, 'audit-list')[0].getAttribute('aria-busy')).toBe('true');
  });

  it('uses the singular summary and the end line when everything is loaded', () => {
    const { root } = setup([ev(1)]);
    expect(q(root, 'audit-summary')[0].textContent?.trim()).toBe('1 event');
    expect(q(root, 'audit-no-more')[0].textContent).toContain('No more events');
  });

  it('shows current values with a notice for a non-created event without previous payload', () => {
    const { root } = setup([ev(2, { payload: { code: 'A' }, previousPayload: null })]);
    expect(q(root, 'audit-previous-unavailable')[0].textContent).toContain('Previous version unavailable');
    expect(q(root, 'audit-initial')[0].querySelectorAll('tbody tr')).toHaveLength(1);
  });
});

describe('NxAuditTrailComponent language switch with an async loader', () => {
  class DelayedLoader implements TranslocoLoader {
    getTranslation(lang: string): Observable<Record<string, unknown>> {
      const de = { ...EN, events: { org: { warehouse: { updated: 'Lager aktualisiert' } } } };
      return lang === 'de' ? of(de).pipe(delay(20)) : of(EN);
    }
  }

  it('re-renders translated titles only after the new language has loaded', async () => {
    TestBed.configureTestingModule({
      imports: [NxAuditTrailComponent],
      providers: [
        provideNoopAnimations(),
        ...provideTransloco({
          config: { availableLangs: ['en', 'de'], defaultLang: 'en', reRenderOnLangChange: true },
          loader: DelayedLoader,
        }),
      ],
    });
    const facade = {
      items: signal([ev(1)]),
      total: signal(1),
      loading: signal(false),
      error: signal(false),
      hasMore: signal(false),
      resolvedLabels: signal({}),
      load: vi.fn().mockResolvedValue(undefined),
      reload: vi.fn(),
      loadMore: vi.fn(),
      retry: vi.fn(),
    };
    TestBed.overrideComponent(NxAuditTrailComponent, { set: { providers: [{ provide: AuditTrailFacade, useValue: facade }] } });
    const fixture = TestBed.createComponent(NxAuditTrailComponent);
    fixture.componentRef.setInput('aggregateType', 'warehouse');
    fixture.componentRef.setInput('aggregateId', 'w-1');
    const root = fixture.nativeElement as HTMLElement;
    const title = () => root.querySelector('[data-testid="audit-title"]')?.textContent?.trim();

    fixture.detectChanges();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(title()).toBe('Warehouse updated');
    });

    TestBed.inject(TranslocoService).setActiveLang('de');
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(title()).toBe('Lager aktualisiert');
    });
  });
});
