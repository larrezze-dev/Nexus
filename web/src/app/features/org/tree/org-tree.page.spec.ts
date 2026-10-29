import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { OrgTreeFacade } from './org-tree.facade';
import { OrgTreeNode } from './org-tree.model';
import { OrgTreePage } from './org-tree.page';

describe('OrgTreePage', () => {
  function setup(facadeOverrides: Partial<OrgTreeFacade>) {
    const load = vi.fn();
    TestBed.configureTestingModule({
      imports: [OrgTreePage],
      providers: [
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
        {
          provide: OrgTreeFacade,
          useValue: {
            load,
            nodes: signal<OrgTreeNode[]>([]),
            loading: signal(false),
            error: signal<string | null>(null),
            ...facadeOverrides,
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(OrgTreePage);
    fixture.detectChanges();
    return { fixture, load };
  }

  it('calls facade.load() on init and renders the assembled tree, expanded by default', () => {
    const warehouse: OrgTreeNode = {
      id: 'wh1',
      kind: 'warehouse',
      code: 'WH-01',
      label: 'Zürich Warehouse',
      isActive: true,
      children: [],
    };
    const legalEntity: OrgTreeNode = {
      id: 'le1',
      kind: 'legalEntity',
      code: 'LE-CH',
      label: 'LE-CH',
      isActive: true,
      children: [warehouse],
    };
    const group: OrgTreeNode = {
      id: 'g1',
      kind: 'group',
      code: 'NUSSLI',
      label: 'NUSSLI',
      isActive: null,
      children: [legalEntity],
    };

    const { fixture, load } = setup({ nodes: signal([group]) });

    expect(load).toHaveBeenCalledOnce();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('NUSSLI');
    expect(text).toContain('LE-CH');
    expect(text).toContain('Zürich Warehouse');
  });

  it('shows the error message instead of the tree when the facade failed to load', () => {
    const { fixture } = setup({ error: signal('boom') });
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('boom');
  });
});
