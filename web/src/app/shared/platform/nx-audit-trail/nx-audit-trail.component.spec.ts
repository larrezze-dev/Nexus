import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { AuditEvent } from '../../../api/platform/models/audit-event';
import { AuditTrailFacade } from './audit-trail.facade';
import { NxAuditTrailComponent } from './nx-audit-trail.component';

const EN = {
  common: { yes: 'Yes', no: 'No', reload: 'Reload' },
  audit: {
    summary: '{{count}} events',
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

function setup(items: AuditEvent[], extra: { loading?: boolean; error?: string | null } = {}) {
  const facade = {
    items: signal(items),
    total: signal(items.length),
    loading: signal(extra.loading ?? false),
    error: signal(extra.error ?? null),
    hasMore: signal(false),
    resolvedLabels: signal<Record<string, string>>({}),
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
    expect(root.querySelectorAll('th[scope="col"]').length).toBeGreaterThan(0);
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
    const { root, facade } = setup([], { error: 'boom' });
    expect(q(root, 'audit-error')[0].getAttribute('role')).toBe('alert');
    q(root, 'audit-retry')[0].click();
    expect(facade.reload).toHaveBeenCalled();
  });
});
