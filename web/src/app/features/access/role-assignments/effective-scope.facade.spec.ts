import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Api } from '../../../api/platform/api';
import { OrgTreeFacade } from '../../org/tree/org-tree.facade';
import { OrgTreeNode } from '../../org/tree/org-tree.model';
import { EffectiveScopeFacade } from './effective-scope.facade';
import { directScopeOf, scopeStateOf } from './effective-scope.model';

const wh: OrgTreeNode = { id: 'wh1', kind: 'warehouse', code: 'WH1', isActive: true, children: [] };
const bu: OrgTreeNode = { id: 'bu1', kind: 'businessUnit', code: 'BU1', isActive: true, children: [wh] };
const le: OrgTreeNode = { id: 'le1', kind: 'legalEntity', code: 'LE1', isActive: true, children: [bu] };
const grp: OrgTreeNode = { id: 'g1', kind: 'group', code: 'G', isActive: null, children: [le] };

describe('scopeStateOf', () => {
  it('tells direct from inherited and marks the way down as context', () => {
    const visible = new Set(['bu1', 'wh1']);
    const direct = directScopeOf([{ scopeType: 'BUSINESS_UNIT', scopeEntityId: 'bu1' }]);
    expect(scopeStateOf(bu, visible, direct)).toBe('direct');
    expect(scopeStateOf(wh, visible, direct)).toBe('inherited');
    expect(scopeStateOf(le, visible, direct)).toBe('context');
    expect(scopeStateOf(grp, visible, direct)).toBe('context');
    expect(scopeStateOf(le, new Set(), directScopeOf([]))).toBe('none');
  });

  it('a GLOBAL assignment makes every visible node direct, ignores PROJECT assignments', () => {
    const direct = directScopeOf([
      { scopeType: 'GLOBAL', scopeEntityId: null },
      { scopeType: 'PROJECT', scopeEntityId: 'p1' },
    ]);
    expect(direct.ids.has('p1')).toBe(false);
    expect(scopeStateOf(wh, new Set(['wh1']), direct)).toBe('direct');
    expect(scopeStateOf(grp, new Set(['wh1']), direct)).toBe('direct');
  });
});

describe('EffectiveScopeFacade', () => {
  function setup() {
    const resolvers: Array<(v: unknown) => void> = [];
    const calls: string[] = [];
    const api = {
      invoke: vi.fn((fn: { name?: string }, params: { id?: string; userId?: string }) => {
        const user = params.id ?? params.userId ?? '';
        calls.push(user);
        return new Promise((resolve) => resolvers.push(resolve));
      }),
    };
    const tree = {
      load: vi.fn().mockResolvedValue(undefined),
      error: signal<string | null>(null),
      nodes: signal<OrgTreeNode[]>([grp]),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: Api, useValue: api },
        { provide: OrgTreeFacade, useValue: tree },
      ],
    });
    return { facade: TestBed.inject(EffectiveScopeFacade), api, tree, resolvers };
  }

  it('loads scope, assignments, user name and tree together', async () => {
    const { facade, api, resolvers } = setup();
    const load = facade.load('u1');
    expect(api.invoke).toHaveBeenCalledTimes(3);
    // order: scope, assignments, user
    resolvers[0]({ legalEntityIds: [], businessUnitIds: ['bu1'], warehouseIds: ['wh1'], projectIds: ['p1'] });
    resolvers[1]({ items: [{ scopeType: 'BUSINESS_UNIT', scopeEntityId: 'bu1' }] });
    resolvers[2]({ fullName: 'Ada Lovelace' });
    await load;

    expect(facade.loaded()).toBe(true);
    expect(facade.userName()).toBe('Ada Lovelace');
    expect(facade.projectIds()).toEqual(['p1']);
    expect(facade.stateOf(bu)).toBe('direct');
    expect(facade.stateOf(wh)).toBe('inherited');
    expect(facade.isEmpty()).toBe(false);
  });

  it('keeps the tree when the assignments or the user cannot be read', async () => {
    const { facade, resolvers } = setup();
    const load = facade.load('u1');
    resolvers[0]({ legalEntityIds: [], businessUnitIds: [], warehouseIds: ['wh1'], projectIds: [] });
    resolvers[1](null);
    resolvers[2](null);
    await load;
    expect(facade.loaded()).toBe(true);
    expect(facade.directKnown()).toBe(false);
    expect(facade.userName()).toBeNull();
    expect(facade.stateOf(wh)).toBe('inherited');
  });

  it('a failure sets the error and clears the data; empty only when loaded', async () => {
    const { facade, tree, resolvers } = setup();
    tree.error.set('boom');
    const load = facade.load('u1');
    resolvers[0]({ legalEntityIds: [], businessUnitIds: [], warehouseIds: [], projectIds: [] });
    resolvers[1](null);
    resolvers[2](null);
    await load;

    expect(facade.error()).toBe('load_failed');
    expect(facade.loaded()).toBe(false);
    expect(facade.nodes()).toEqual([]);
  });

  it('a late response of a previous user never overwrites the current one', async () => {
    const { facade, resolvers } = setup();
    const first = facade.load('u1');
    const second = facade.load('u2');
    const scope = (id: string) => ({ legalEntityIds: [], businessUnitIds: [], warehouseIds: [id], projectIds: [] });
    // calls 0-2 belong to u1, 3-5 to u2
    resolvers[3](scope('wh2'));
    resolvers[4](null);
    resolvers[5]({ fullName: 'Second' });
    await second;
    resolvers[0](scope('wh1'));
    resolvers[1](null);
    resolvers[2]({ fullName: 'First' });
    await first;

    expect(facade.userName()).toBe('Second');
    expect(facade.stateOf({ ...wh, id: 'wh2' })).toBe('inherited');
    expect(facade.stateOf({ ...wh, id: 'wh1' })).toBe('none');
    expect(facade.loading()).toBe(false);
  });
});
