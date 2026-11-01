import { ComponentFixture } from '@angular/core/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { OrgTreeFacade } from '../../org/tree/org-tree.facade';
import { OrgTreeNode } from '../../org/tree/org-tree.model';
import { EffectiveScopePage } from './effective-scope.page';
import { RoleAssignmentsFacade } from './role-assignments.facade';

const userId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0001';

function setup(
  nodes: OrgTreeNode[],
  effectiveScope = { legalEntityIds: [] as string[], businessUnitIds: [] as string[], warehouseIds: [] as string[], projectIds: [] as string[] },
) {
  const orgTreeFacade = {
    load: vi.fn().mockResolvedValue(undefined),
    nodes: signal(nodes),
    loading: signal(false),
    error: signal<string | null>(null),
  };
  const assignmentsFacade = {
    getEffectiveScope: vi.fn().mockResolvedValue(effectiveScope),
  };

  TestBed.configureTestingModule({
    imports: [EffectiveScopePage],
    providers: [
      provideNoopAnimations(),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: OrgTreeFacade, useValue: orgTreeFacade },
      { provide: RoleAssignmentsFacade, useValue: assignmentsFacade },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: userId }) } } },
    ],
  });

  const fixture = TestBed.createComponent(EffectiveScopePage);
  return { fixture, orgTreeFacade, assignmentsFacade };
}

/**
 * `ngOnInit`'s two sequential `await`s (`orgTreeFacade.load()` then
 * `assignmentsFacade.getEffectiveScope()`) don't reliably settle within a single
 * `fixture.whenStable()` wait in this test environment, unlike zone-tracked HTTP calls
 * elsewhere in the app — so this polls a few microtask/macrotask turns instead of asserting
 * on the very next tick.
 */
async function flushUntilLoaded(fixture: ComponentFixture<EffectiveScopePage>): Promise<void> {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    if (!fixture.componentInstance['loading']()) {
      return;
    }
  }
}

describe('EffectiveScopePage', () => {
  it('loads the org tree and the effective scope for the user id in the route', async () => {
    const warehouse: OrgTreeNode = { id: 'wh1', kind: 'warehouse', code: 'WH-01', label: 'Zürich', isActive: true, children: [] };
    const legalEntity: OrgTreeNode = { id: 'le1', kind: 'legalEntity', code: 'LE-CH', label: 'LE-CH', isActive: true, children: [warehouse] };
    const group: OrgTreeNode = { id: 'g1', kind: 'group', code: 'NUSSLI', label: 'NUSSLI', isActive: null, children: [legalEntity] };

    const { fixture, orgTreeFacade, assignmentsFacade } = setup([group], {
      legalEntityIds: ['le1'],
      businessUnitIds: [],
      warehouseIds: [],
      projectIds: [],
    });

    fixture.detectChanges();
    await flushUntilLoaded(fixture);

    expect(orgTreeFacade.load).toHaveBeenCalledOnce();
    expect(assignmentsFacade.getEffectiveScope).toHaveBeenCalledWith(userId);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('LE-CH');
    expect(fixture.componentInstance['isVisible'](legalEntity)).toBe(true);
    expect(fixture.componentInstance['isVisible'](warehouse)).toBe(false);
    // A Group is visible when at least one descendant is, even though it isn't itself a
    // grantable scope type.
    expect(fixture.componentInstance['isVisible'](group)).toBe(true);
  });

  it('shows the empty-state message, not an error, when projectIds is empty', async () => {
    const { fixture } = setup([]);

    fixture.detectChanges();
    await flushUntilLoaded(fixture);

    const empty = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="effective-scope-projects-empty"]');
    expect(empty).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
