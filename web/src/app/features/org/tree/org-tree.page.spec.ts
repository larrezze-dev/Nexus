import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it, vi } from 'vitest';
import { OrgTreeFacade } from './org-tree.facade';
import { OrgTreeNode } from './org-tree.model';
import { OrgTreePage } from './org-tree.page';

const warehouse: OrgTreeNode = {
  id: 'wh1',
  kind: 'warehouse',
  code: 'WH-01',
  captionI18n: { de: 'Lager Zug', es: 'Almacén Zug' },
  name: null,
  label: 'Lager Zug',
  isActive: false,
  children: [],
};
const businessUnit: OrgTreeNode = {
  id: 'bu1',
  kind: 'businessUnit',
  code: 'BU-EV',
  captionI18n: { de: 'Events', es: 'Eventos' },
  name: null,
  label: 'Events',
  isActive: true,
  children: [warehouse],
};
const legalEntity: OrgTreeNode = {
  id: 'le1',
  kind: 'legalEntity',
  code: 'LE-CH',
  captionI18n: null,
  name: 'SSB Schweiz',
  label: 'SSB Schweiz',
  isActive: true,
  children: [businessUnit],
};
const group: OrgTreeNode = {
  id: 'g1',
  kind: 'group',
  code: 'SSB',
  captionI18n: null,
  name: null,
  label: 'SSB',
  isActive: null,
  children: [legalEntity],
};

const tree = {
  title: 'Organization tree',
  subtitle: 'How the group is organized.',
  empty: 'No organization structure yet.',
  emptyHint: 'It appears here later.',
  treeLabel: 'Organization structure',
  expand: 'Expand «{{name}}»',
  collapse: 'Collapse «{{name}}»',
  kind: {
    group: 'Group',
    legalEntity: 'Legal entity',
    businessUnit: 'Business unit',
    warehouse: 'Warehouse',
  },
};
const langs = {
  en: { org: { tree }, common: { retry: 'Retry' } },
  es: {
    org: {
      tree: {
        ...tree,
        title: 'Árbol de la organización',
        kind: { ...tree.kind, warehouse: 'Almacén' },
      },
    },
    common: { retry: 'Reintentar' },
  },
};

interface FacadeState {
  readonly nodes?: OrgTreeNode[];
  readonly loading?: boolean;
  readonly error?: string | null;
}

async function setup(state: FacadeState = {}) {
  const load = vi.fn();
  await TestBed.configureTestingModule({
    imports: [
      OrgTreePage,
      TranslocoTestingModule.forRoot({
        langs,
        translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      {
        provide: OrgTreeFacade,
        useValue: {
          load,
          nodes: signal(state.nodes ?? [group]),
          loading: signal(state.loading ?? false),
          error: signal(state.error ?? null),
        },
      },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<OrgTreePage> = TestBed.createComponent(OrgTreePage);
  // A few rounds: the tree renders the nested levels (and hands them to its key manager) one at a time.
  for (let round = 0; round < 4; round++) {
    fixture.detectChanges();
    await fixture.whenStable();
  }
  return { fixture, load };
}

function root(fixture: ComponentFixture<OrgTreePage>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function node(fixture: ComponentFixture<OrgTreePage>, id: string): HTMLElement {
  return root(fixture).querySelector(`[data-testid="org-tree-node-${id}"]`) as HTMLElement;
}

describe('OrgTreePage', () => {
  it('loads on init and uses the page template: breadcrumb, one h1, subtitle', async () => {
    const { fixture, load } = await setup();

    expect(load).toHaveBeenCalledOnce();
    expect(root(fixture).querySelector('nx-page-header app-breadcrumb')).toBeTruthy();
    expect(root(fixture).querySelectorAll('h1').length).toBe(1);
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('Organization tree');
    expect(root(fixture).querySelector('.nx-page-header__subtitle')?.textContent?.trim()).toBe(
      'How the group is organized.',
    );
  });

  it('renders an ARIA tree, fully expanded, with labelled treeitems and their levels', async () => {
    const { fixture } = await setup();

    const treeEl = root(fixture).querySelector('[role="tree"]');
    expect(treeEl?.getAttribute('aria-label')).toBe('Organization structure');
    const items = Array.from(root(fixture).querySelectorAll('[role="treeitem"]'));
    expect(items.length).toBe(4);
    expect(items.map((item) => item.getAttribute('aria-level'))).toEqual(['1', '2', '3', '4']);
    expect(node(fixture, 'g1').getAttribute('aria-expanded')).toBe('true');
    expect(node(fixture, 'wh1').getAttribute('aria-expanded')).toBeNull();
    // One tab stop: only the first item is focusable by Tab; toggles and links are out of the tab order.
    expect(items.filter((item) => item.getAttribute('tabindex') === '0').length).toBe(1);
    expect(
      Array.from(root(fixture).querySelectorAll('[role="tree"] a, [role="tree"] button')).every(
        (el) => el.getAttribute('tabindex') === '-1',
      ),
    ).toBe(true);
  });

  it('names each node in the active language and says what it is', async () => {
    const { fixture } = await setup();

    expect(node(fixture, 'g1').querySelector('.nx-org-tree__name')?.textContent?.trim()).toBe(
      'SSB',
    );
    expect(node(fixture, 'le1').querySelector('.nx-org-tree__name')?.textContent?.trim()).toBe(
      'SSB Schweiz',
    );
    expect(node(fixture, 'bu1').querySelector('.nx-org-tree__name')?.textContent?.trim()).toBe(
      'Events',
    );
    expect(
      node(fixture, 'wh1')
        .querySelector('.nx-org-tree__meta')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim(),
    ).toBe('Warehouse · WH-01');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(node(fixture, 'bu1').querySelector('.nx-org-tree__name')?.textContent?.trim()).toBe(
      'Eventos',
    );
    expect(node(fixture, 'wh1').querySelector('.nx-org-tree__name')?.textContent?.trim()).toBe(
      'Almacén Zug',
    );
    expect(node(fixture, 'wh1').querySelector('.nx-org-tree__meta')?.textContent).toContain(
      'Almacén',
    );
  });

  it('links every node with a page to its detail; the group has no link', async () => {
    const { fixture } = await setup();

    expect(node(fixture, 'g1').querySelector(':scope > .nx-org-tree__row a')).toBeNull();
    expect(node(fixture, 'le1').querySelector('a')?.getAttribute('href')).toBe(
      '/legal-entities/le1',
    );
    expect(node(fixture, 'bu1').querySelector('a')?.getAttribute('href')).toBe(
      '/business-units/bu1',
    );
    expect(node(fixture, 'wh1').querySelector('a')?.getAttribute('href')).toBe('/warehouses/wh1');
  });

  it('moves with the arrow keys and opens the detail page with Enter', async () => {
    const { fixture } = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const key = (target: HTMLElement, k: string) =>
      target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }));

    // Focus starts on the first item (the group); ↓ ↓ reaches the business unit.
    key(node(fixture, 'g1'), 'ArrowDown');
    key(node(fixture, 'le1'), 'ArrowDown');
    fixture.detectChanges();
    expect(node(fixture, 'bu1').getAttribute('tabindex')).toBe('0');

    key(node(fixture, 'bu1'), 'Enter');
    expect(navigate).toHaveBeenCalledWith(['/business-units', 'bu1']);
  });

  it('collapses and expands a node with its toggle (translated label)', async () => {
    const { fixture } = await setup();
    const toggle = node(fixture, 'le1').querySelector('button') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-label')).toBe('Collapse «SSB Schweiz»');

    toggle.click();
    fixture.detectChanges();

    expect(node(fixture, 'le1').getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('Expand «SSB Schweiz»');
  });

  it('shows skeleton rows while loading', async () => {
    const { fixture } = await setup({ loading: true });
    expect(root(fixture).querySelector('[data-testid="org-tree-loading"]')).toBeTruthy();
    expect(root(fixture).querySelector('[role="tree"]')).toBeNull();
  });

  it('shows the error state with «Retry» instead of the tree', async () => {
    const { fixture, load } = await setup({ error: 'boom', nodes: [] });

    expect(root(fixture).querySelector('[data-testid="org-tree-error"]')).toBeTruthy();
    expect(root(fixture).textContent).not.toContain('boom');
    root(fixture)
      .querySelector<HTMLButtonElement>('[data-testid="org-tree-error"] button')!
      .click();
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('shows the empty state when there is no structure', async () => {
    const { fixture } = await setup({ nodes: [] });
    const empty = root(fixture).querySelector('[data-testid="org-tree-empty"]');
    expect(empty?.textContent).toContain('No organization structure yet.');
    expect(empty?.textContent).toContain('It appears here later.');
  });
});
