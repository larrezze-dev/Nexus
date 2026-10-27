import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
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
});
