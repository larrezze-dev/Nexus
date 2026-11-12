import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
import { routes as appRoutes } from '../../app.routes';
import { NavigationBuilderService } from './navigation-builder.service';

describe('NavigationBuilderService', () => {
  let service: NavigationBuilderService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(NavigationBuilderService);
  });

  it('builds a nav item for every top-level route that has data.titleKey', () => {
    const routes: Routes = [
      { path: 'account', data: { titleKey: 'nav.account', icon: 'person' } },
      { path: '**', children: [] },
    ];

    const items = service.build(routes);

    expect(items).toEqual([{ titleKey: 'nav.account', path: '/account', icon: 'person', permission: undefined }]);
  });

  it('includes the permission from data.permission when present', () => {
    const routes: Routes = [
      { path: 'asset-types', data: { titleKey: 'nav.assetTypes', icon: 'category', permission: 'ASSET_TYPE:READ' } },
    ];

    const items = service.build(routes);

    expect(items[0].permission).toBe('ASSET_TYPE:READ');
  });

  it('builds nested items from children and joins their paths', () => {
    const routes: Routes = [
      {
        path: 'catalog',
        data: { titleKey: 'nav.catalog', icon: 'inventory' },
        children: [{ path: 'types', data: { titleKey: 'nav.catalogTypes', icon: 'category' } }],
      },
    ];

    const items = service.build(routes);

    expect(items).toEqual([
      {
        titleKey: 'nav.catalog',
        path: '/catalog',
        icon: 'inventory',
        permission: undefined,
        children: [{ titleKey: 'nav.catalogTypes', path: '/catalog/types', icon: 'category', permission: undefined }],
      },
    ]);
  });

  it('hoists children of a route without its own titleKey to the parent level', () => {
    const routes: Routes = [
      {
        path: 'catalog',
        children: [{ path: 'types', data: { titleKey: 'nav.catalogTypes', icon: 'category' } }],
      },
    ];

    const items = service.build(routes);

    expect(items).toEqual([{ titleKey: 'nav.catalogTypes', path: '/catalog/types', icon: 'category', permission: undefined }]);
  });

  it('skips routes without data.titleKey and without nav-worthy children', () => {
    const routes: Routes = [{ path: '**', data: {} }];

    expect(service.build(routes)).toEqual([]);
  });

  it('skips routes with data.nav === false even when they have a titleKey', () => {
    const routes: Routes = [
      { path: 'account', data: { titleKey: 'nav.account', icon: 'person', nav: false } },
      { path: 'users', data: { titleKey: 'nav.users', navGroup: 'people' } },
    ];

    expect(service.build(routes).map((item) => item.path)).toEqual(['/users']);
  });

  it('lists a componentless parent once when titleKey lives on the list child only', () => {
    const routes: Routes = [
      {
        path: 'users',
        children: [
          { path: '', pathMatch: 'full', data: { titleKey: 'nav.users', navGroup: 'people' } },
          { path: 'new', data: { listTitleKey: 'nav.users', crumbKey: 'breadcrumb.new.user' } },
          { path: ':id', data: { listTitleKey: 'nav.users', detail: true } },
        ],
      },
    ];

    expect(service.build(routes)).toEqual([
      { titleKey: 'nav.users', path: '/users', icon: undefined, permission: undefined, navGroup: 'people' },
    ]);
  });

  it('groups items by navGroup in display order and drops empty groups', () => {
    const routes: Routes = [
      { path: 'roles', data: { titleKey: 'nav.roles', navGroup: 'access' } },
      { path: 'users', data: { titleKey: 'nav.users', navGroup: 'people' } },
      { path: 'tree', data: { titleKey: 'nav.tree', navGroup: 'org' } },
      { path: 'locations', data: { titleKey: 'nav.locations', navGroup: 'org' } },
    ];

    const groups = service.buildGrouped(routes);

    expect(groups.map((g) => [g.navGroup, g.titleKey, g.items.map((i) => i.path)])).toEqual([
      ['org', 'nav.groups.org', ['/tree', '/locations']],
      ['people', 'nav.groups.people', ['/users']],
      ['access', 'nav.groups.access', ['/roles']],
    ]);
  });

  it('real app routes: one entry per section, in the approved order, without My account', () => {
    const items = service.build(appRoutes);

    expect(items.map((item) => item.path)).toEqual([
      '/org/tree',
      '/legal-entities',
      '/business-units',
      '/locations',
      '/warehouses',
      '/org/currencies',
      '/users',
      '/parties',
      '/roles',
    ]);
    expect(service.buildGrouped(appRoutes).map((g) => [g.navGroup, g.items.length])).toEqual([
      ['org', 6],
      ['people', 2],
      ['access', 1],
    ]);
  });
});
