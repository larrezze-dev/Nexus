import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { OrgTreeNode } from '../../org/tree/org-tree.model';
import { ScopeNodeState } from './effective-scope.model';
import { EffectiveScopeFacade } from './effective-scope.facade';
import { EffectiveScopePage } from './effective-scope.page';

const userId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0001';

function setup(options: { nodes?: OrgTreeNode[]; states?: Record<string, ScopeNodeState>; empty?: boolean; error?: string | null; userName?: string | null; projectIds?: string[]; directKnown?: boolean } = {}) {
  const facade = {
    loading: signal(false),
    error: signal<string | null>(options.error ?? null),
    loaded: signal(!options.error),
    userName: signal<string | null>(options.userName ?? null),
    nodes: signal(options.nodes ?? []),
    projectIds: signal(options.projectIds ?? []),
    directKnown: signal(options.directKnown ?? true),
    isEmpty: signal(options.empty ?? false),
    stateOf: (node: OrgTreeNode): ScopeNodeState => options.states?.[node.id] ?? 'none',
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
  };
  const breadcrumb = { setCurrentLabel: vi.fn(), setLoadFailed: vi.fn(), clearLoadFailed: vi.fn() };
  TestBed.configureTestingModule({
    imports: [EffectiveScopePage],
    providers: [
      provideNoopAnimations(),
      ...provideTransloco({ config: { availableLangs: ['en', 'es'], defaultLang: 'en' } }),
      { provide: EffectiveScopeFacade, useValue: facade },
      { provide: NxBreadcrumbService, useValue: breadcrumb },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: userId }) } } },
    ],
  });
  const fixture = TestBed.createComponent(EffectiveScopePage);
  return { fixture, facade, breadcrumb };
}

const warehouse: OrgTreeNode = { id: 'wh1', kind: 'warehouse', code: 'WH-01', captionI18n: { de: 'Lager Zug', es: 'Almacén Zug' }, isActive: true, children: [] };
const group: OrgTreeNode = { id: 'g1', kind: 'group', code: 'SSB', name: 'SAPE Holding', isActive: null, children: [warehouse] };

describe('EffectiveScopePage', () => {
  it('resets and loads the facade for the user id in the route', () => {
    const { fixture, facade } = setup();
    fixture.detectChanges();
    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(userId);
  });

  it('names the nodes in the active language and follows a language switch', () => {
    const { fixture } = setup({ nodes: [group], states: { wh1: 'direct', g1: 'context' } });
    fixture.detectChanges();
    const labels = () =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.nx-effective-scope-tree__label')).map((n) => n.textContent?.trim());

    expect(labels()).toEqual(['SAPE Holding', 'Lager Zug']);
    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(labels()).toEqual(['SAPE Holding', 'Almacén Zug']);
  });

  it('marks each node with its state and shows the legend', () => {
    const { fixture } = setup({ nodes: [group], states: { g1: 'context', wh1: 'inherited' } });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    const states = Array.from(el.querySelectorAll('[data-state]')).map((n) => n.getAttribute('data-state'));
    expect(states).toEqual(['context', 'inherited']);
    expect(el.querySelector('[data-testid="effective-scope-legend"]')).not.toBeNull();
    for (const state of ['direct', 'inherited', 'context', 'none']) {
      expect(el.querySelector(`[data-testid="effective-scope-legend-${state}"]`)).not.toBeNull();
    }
  });

  it('leaves the inherited entry out of the legend when the assignments could not be read', () => {
    const { fixture } = setup({ nodes: [group], directKnown: false });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-legend-inherited"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-legend-direct"]')).not.toBeNull();
  });

  it('puts the user name in the breadcrumb and the page', () => {
    const { fixture, breadcrumb } = setup({ nodes: [group], userName: 'Ada Lovelace' });
    fixture.detectChanges();
    expect(breadcrumb.setCurrentLabel).toHaveBeenCalledWith(expect.stringContaining('Ada Lovelace'));
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-user"]').textContent).toContain('Ada Lovelace');
  });

  it('shows an error with a retry (not the empty state) when the load failed, and retries', async () => {
    const { fixture, facade, breadcrumb } = setup({ error: 'load_failed' });
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('[data-testid="effective-scope-error"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="effective-scope-empty"]')).toBeNull();
    expect(breadcrumb.setLoadFailed).toHaveBeenCalled();

    facade.load.mockClear();
    (el.querySelector('[data-testid="effective-scope-retry"]') as HTMLButtonElement).click();
    expect(breadcrumb.clearLoadFailed).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(userId);
  });

  it('shows the overall empty-state message, not an error, when nothing is visible at all', () => {
    const { fixture } = setup({ empty: true });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-empty"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows the projects-specific empty message when only projectIds is empty', () => {
    const { fixture } = setup({ nodes: [group], states: { wh1: 'direct' } });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-projects-empty"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="effective-scope-empty"]')).toBeNull();
  });
});
