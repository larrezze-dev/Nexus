import { PermissionsStore } from './permissions.store';

describe('PermissionsStore', () => {
  it('has() returns true for any permission while the list is empty (step 0.5 behavior)', () => {
    const store = new PermissionsStore();

    expect(store.permissions()).toEqual([]);
    expect(store.has('ASSET_TYPE:CREATE')).toBe(true);
    expect(store.has('ANYTHING:AT_ALL')).toBe(true);
  });

  it('has() checks real membership once the list is populated (future 0.6 behavior)', () => {
    const store = new PermissionsStore();

    store.setPermissions(['ASSET_TYPE:CREATE']);

    expect(store.has('ASSET_TYPE:CREATE')).toBe(true);
    expect(store.has('ASSET_TYPE:DELETE')).toBe(false);
  });
});
