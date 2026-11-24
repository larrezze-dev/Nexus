import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { PermissionGrant } from '../../../api/platform/models/permission-grant';
import { Role } from '../../../api/platform/models/role';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { RoleDetailPage } from './role-detail.page';
import { RolesFacade } from './roles.facade';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';

const role: Role = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'ADMIN',
  captionI18n: { de: 'Administrator' },
  description: null,
  isSystem: false,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const grant: PermissionGrant = {
  code: 'ORG_STRUCTURE:CREATE',
  action: 'CREATE',
  resourceType: 'ORG_STRUCTURE',
  captionI18n: { de: 'Anlegen' },
  validFrom: '2026-01-01T00:00:00Z',
  validTo: null,
};

const translations = {
  en: {
    access: {
      roles: {
        newTitle: 'New role',
        loading: 'Loading…',
        confirmDeactivate: 'Are you sure?',
        fields: {},
        tabs: { general: 'General', permissionHistory: 'Permission history' },
        actions: { deactivate: 'Deactivate', save: 'Save' },
        permissionMatrix: {
          asOf: 'As of',
          resource: 'Resource',
          discardDialog: { title: 'Discard', message: 'Discard?', confirm: 'Discard and continue' },
        },
        deactivateDialog: { title: 'Deactivate role', message: 'Deactivate {{name}}?' },
      },
      permissions: {
        resources: { ORG_STRUCTURE: 'Organisation structure', AUDIT: 'Audit' },
        actions: { READ: 'View', CREATE: 'Create' },
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
  },
};

function createFacadeStub(initialSelected: Role | null) {
  const selected = signal(initialSelected);
  return {
    selected: selected.asReadonly(),
    setSelected: (next: Role | null) => selected.set(next),
    loading: signal(false).asReadonly(),
    error: signal<string | null>(null).asReadonly(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
    listAllPermissions: vi.fn().mockResolvedValue([]),
    getPermissions: vi.fn().mockResolvedValue([]),
    savePermissions: vi.fn().mockResolvedValue([]),
  };
}

async function setupPage(paramMap: Record<string, string>, facade: ReturnType<typeof createFacadeStub>) {
  await TestBed.configureTestingModule({
    imports: [
      RoleDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      }),
    ],
    providers: [
      provideNoopAnimations(),
      provideNativeDateAdapter(),
      { provide: RolesFacade, useValue: facade },
      { provide: NxI18nService, useValue: {
          activeLang: () => 'en',
          getActiveLanguage: () => 'en',
          translateErrorCode: (code: string | null | undefined) => code ?? 'generic',
        },
      },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
      {
        provide: NxBreadcrumbService,
        useValue: {
          setCurrentLabel: vi.fn(),
          setLoadFailed: vi.fn(),
          clearLoadFailed: vi.fn(),
          claimForPage: () => () => undefined,
          trail: signal([]),
        },
      },
      { provide: NxConfirm, useValue: { confirm: vi.fn().mockResolvedValue(true) } },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<RoleDetailPage> = TestBed.createComponent(RoleDetailPage);
  return fixture;
}

describe('RoleDetailPage', () => {
  it('loads the role and its permission catalog/grants in edit mode', async () => {
    const facade = createFacadeStub(role);
    facade.getPermissions.mockResolvedValue([grant]);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(role.id);
    expect(facade.listAllPermissions).toHaveBeenCalled();
    expect(facade.getPermissions).toHaveBeenCalledWith(role.id, undefined);
    expect(fixture.componentInstance['grantedCodes']().has(grant.code)).toBe(true);
  });

  it('does not load anything in create mode (no id param)', async () => {
    const facade = createFacadeStub(null);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    expect(facade.load).not.toHaveBeenCalled();
    expect(facade.listAllPermissions).not.toHaveBeenCalled();
  });

  it('disables the code control in edit mode (UpdateRoleRequest has no code field)', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance['form'].controls.code.disabled).toBe(true);
  });

  it('sends captionI18n/description on update without a code field', async () => {
    const facade = createFacadeStub(role);
    facade.update.mockResolvedValue(role);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    await fixture.componentInstance['onSave']();

    expect(facade.update).toHaveBeenCalledWith(role.id, {
      captionI18n: role.captionI18n,
      description: null,
    });
  });

  it('maps a 400 response into per-field errors on create', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockRejectedValue({ status: 400, error: { errors: [{ field: 'Code', message: 'Code already in use' }] } });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    fixture.componentInstance['form'].controls.code.setValue('ADMIN');
    fixture.componentInstance['form'].controls.captionI18n.setValue({ de: 'Admin' });

    await fixture.componentInstance['onSave']();

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['shared.form.invalidValue'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
  });

  it('409 CONFLICT (duplicate code) marks the code field; 409 CONCURRENCY shows nothing', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockRejectedValueOnce({
      status: 409,
      error: { type: 'https://nexus.nussli.ch/problems/conflict', code: 'CONFLICT', status: 409 },
    });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    fixture.componentInstance['form'].controls.code.setValue('ADMIN');
    fixture.componentInstance['form'].controls.captionI18n.setValue({ de: 'Admin' });

    await fixture.componentInstance['onSave']();
    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['errors.CONFLICT'] });

    facade.create.mockRejectedValueOnce({
      status: 409,
      error: { type: 'https://nexus.nussli.ch/problems/conflict', code: 'CONCURRENCY', status: 409 },
    });
    await fixture.componentInstance['onSave']();
    expect(fixture.componentInstance['fieldErrors']()).toEqual({});
    expect(fixture.componentInstance['saveError']()).toBeNull();
  });

  it('full-replaces permission grants on savePermissions with the toggled codes', async () => {
    const facade = createFacadeStub(role);
    facade.getPermissions.mockResolvedValue([]);
    facade.savePermissions.mockResolvedValue([grant]);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentInstance['allPermissions'].set([
      { code: grant.code, action: 'CREATE', resourceType: 'ORG_STRUCTURE', captionI18n: { de: 'Anlegen' } },
    ]);
    fixture.componentInstance['toggleGrant']({ code: grant.code });

    await fixture.componentInstance['onSavePermissions']();

    expect(facade.savePermissions).toHaveBeenCalledWith(role.id, [grant.code]);
  });

  // asOf = last instant of the picked day in Europe/Zurich (UTC+1 in January), so same-day changes show up.
  it('makes the permission matrix read-only once a past asOf date is picked', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    await fixture.componentInstance['onAsOfChange']('2026-01-01');

    expect(fixture.componentInstance['readOnlyMatrix']()).toBe(true);
    expect(fixture.componentInstance['canEditPermissions']()).toBe(false);
    expect(facade.getPermissions).toHaveBeenCalledWith(role.id, '2026-01-01T22:59:59.999Z');
  });

  describe('SYSTEM_ADMIN locked permissions (ACC-R11)', () => {
    const adminRole: Role = { ...role, code: 'SYSTEM_ADMIN', isSystem: true };
    const critical = ['ROLE:READ', 'ROLE:UPDATE', 'ROLE_ASSIGNMENT:READ', 'ROLE_ASSIGNMENT:CREATE', 'ROLE_ASSIGNMENT:UPDATE'];

    async function open(selected: Role) {
      const facade = createFacadeStub(selected);
      facade.getPermissions.mockResolvedValue([...critical, 'AUDIT:READ'].map((code) => ({ ...grant, code })));
      const fixture = await setupPage({ id: selected.id }, facade);
      fixture.detectChanges();
      await fixture.whenStable();
      return fixture.componentInstance as unknown as {
        isLockedPermission(code: string | undefined): boolean;
        toggleGrant(p: { code: string }): void;
        isGranted(code: string): boolean;
      };
    }

    it('the five critical permissions are locked and cannot be unticked; the others toggle', async () => {
      const page = await open(adminRole);
      for (const code of critical) {
        expect(page.isLockedPermission(code)).toBe(true);
        page.toggleGrant({ code });
        expect(page.isGranted(code)).toBe(true);
      }
      expect(page.isLockedPermission('AUDIT:READ')).toBe(false);
      page.toggleGrant({ code: 'AUDIT:READ' });
      expect(page.isGranted('AUDIT:READ')).toBe(false);
      expect(page.isLockedPermission(undefined)).toBe(false);
    });

    it('no permission is locked on any other role', async () => {
      const page = await open(role);
      expect(critical.some((code) => page.isLockedPermission(code))).toBe(false);
    });
  });

  it('"today" for the as-of picker is the Zurich day, not the browser-local one', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    const page = fixture.componentInstance as unknown as { isToday(v: string): boolean };
    const now = vi.spyOn(Date, 'now');
    // 23:30Z on 14 Nov is already 15 Nov in Zurich (CET); 21:30Z on 14 Jul is still 14 Jul (CEST: 23:30 local).
    now.mockReturnValue(Date.parse('2026-11-14T23:30:00Z'));
    const winter = fixture.componentInstance['now']();
    expect(winter).toBe(Date.parse('2026-11-14T23:30:00Z'));
    expect(page.isToday('2026-11-15')).toBe(true);
    expect(page.isToday('2026-11-14')).toBe(false);
    now.mockReturnValue(Date.parse('2026-07-14T21:30:00Z'));
    expect(page.isToday('2026-07-14')).toBe(true);
    expect(page.isToday('2026-07-15')).toBe(false);
    now.mockRestore();
  });

  it('keeps the matrix editable ("now" semantics) when the picked asOf date is today', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();
    facade.getPermissions.mockClear();

    const now = new Date();
    const todayOnly = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;

    await fixture.componentInstance['onAsOfChange'](todayOnly);

    expect(fixture.componentInstance['asOf']()).toBe('');
    expect(fixture.componentInstance['readOnlyMatrix']()).toBe(false);
    expect(facade.getPermissions).toHaveBeenCalledWith(role.id, undefined);
  });

  const catalog = [
    { code: 'AUDIT:READ', action: 'READ', resourceType: 'AUDIT', captionI18n: { en: 'View audit' } },
    { code: 'ORG_STRUCTURE:READ', action: 'READ', resourceType: 'ORG_STRUCTURE', captionI18n: { en: 'View org' } },
    { code: 'ORG_STRUCTURE:CREATE', action: 'CREATE', resourceType: 'ORG_STRUCTURE', captionI18n: { en: 'Create org' } },
  ] as const;

  async function setupMatrix(grants: string[]) {
    const facade = createFacadeStub(role);
    facade.listAllPermissions.mockResolvedValue([...catalog]);
    facade.getPermissions.mockResolvedValue(grants.map((code) => ({ ...grant, code })));
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();
    return { facade, fixture };
  }

  it('builds the matrix with readable, translated row/column/cell labels', async () => {
    const { fixture } = await setupMatrix([]);
    const matrix = fixture.componentInstance['matrix']();

    expect(matrix.columns.map((c: { label: string }) => c.label)).toEqual(['View', 'Create']);
    expect(matrix.rows.map((r: { label: string }) => r.label)).toEqual(['Audit', 'Organisation structure']);
    expect(matrix.rows[1].cells.get('CREATE')?.label).toBe('Organisation structure — Create');
  });

  it('ticking and unticking cells yields the add/remove diff, and saving sends the full list', async () => {
    const { facade, fixture } = await setupMatrix(['AUDIT:READ']);
    const page = fixture.componentInstance;
    expect(page['permissionsDirty']()).toBe(false);

    page['toggleGrant']({ code: 'ORG_STRUCTURE:READ' });
    page['toggleGrant']({ code: 'AUDIT:READ' });
    expect(page['permissionDiff']()).toEqual({ added: ['ORG_STRUCTURE:READ'], removed: ['AUDIT:READ'] });

    facade.savePermissions.mockResolvedValue([{ ...grant, code: 'ORG_STRUCTURE:READ' }]);
    await page['onSavePermissions']();
    expect(facade.savePermissions).toHaveBeenCalledWith(role.id, ['ORG_STRUCTURE:READ']);
    expect(page['permissionsDirty']()).toBe(false);

    page['toggleGrant']({ code: 'ORG_STRUCTURE:READ' });
    page['onDiscardPermissions']();
    expect(page['permissionsDirty']()).toBe(false);
  });

  /** Opens the «Permissions» tab (tab bodies render only when selected). */
  async function openPermissionsTab(fixture: ComponentFixture<RoleDetailPage>): Promise<HTMLElement> {
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]')[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement;
  }

  it('renders the matrix with labelled Material checkboxes, a dash where the permission does not exist, and a date picker', async () => {
    const { fixture } = await setupMatrix(['AUDIT:READ']);
    const el = await openPermissionsTab(fixture);

    const cell = el.querySelector('mat-checkbox[data-testid="role-permission-cell-ORG_STRUCTURE:CREATE"]');
    expect(cell).not.toBeNull();
    expect(cell?.querySelector('input')?.getAttribute('aria-label')).toBe('Organisation structure — Create');
    expect(el.querySelectorAll('.nx-perm-matrix__cell--none').length).toBe(1); // AUDIT has no CREATE
    expect(el.querySelector('input[type="date"]')).toBeNull();
    expect(el.querySelector('[data-testid="role-permissions-as-of"]')?.hasAttribute('aria-haspopup')).toBe(true);
  });

  it('marks a pending change on the cell (icon + screen-reader text, not only colour)', async () => {
    const { fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    page['toggleGrant']({ code: 'ORG_STRUCTURE:READ' });
    const el = await openPermissionsTab(fixture);

    expect(page['changeOf']('ORG_STRUCTURE:READ')).toBe('added');
    expect(el.querySelectorAll('.nx-perm-matrix__cell--added').length).toBe(1);
    expect(el.querySelector('.nx-perm-matrix__cell--added .nx-perm-matrix__change')?.textContent?.trim()).toBe('add');
  });

  it('shows the matrix with the read-only look and no save bar for a past date', async () => {
    const { fixture } = await setupMatrix(['AUDIT:READ']);
    await fixture.componentInstance['onAsOfChange']('2026-01-01');
    const el = await openPermissionsTab(fixture);

    expect(el.querySelector('.nx-perm-matrix')?.classList.contains('nx-readonly')).toBe(true);
    expect(el.querySelector('[data-testid="role-permissions-history-note"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="role-permissions-save"]')).toBeNull();
  });

  it('a role reload with the same data (permissions saved) keeps unsaved General edits; changed data resets them', async () => {
    const { facade, fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    page['form'].controls.description.setValue('draft text');
    page['form'].markAsDirty();

    facade.setSelected({ ...role, updatedAt: '2026-11-20T10:00:00Z' }); // ETag refresh: same caption/description
    fixture.detectChanges();
    await fixture.whenStable();
    expect(page['form'].controls.description.value).toBe('draft text');
    expect(page['form'].dirty).toBe(true);

    facade.setSelected({ ...role, description: 'saved text', updatedAt: '2026-11-20T11:00:00Z' }); // General saved
    fixture.detectChanges();
    await fixture.whenStable();
    expect(page['form'].controls.description.value).toBe('saved text');
    expect(page['form'].dirty).toBe(false);
  });

  it('changing the as-of date with unsaved matrix edits asks first; cancelling keeps date and draft', async () => {
    const { facade, fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    const confirm = TestBed.inject(NxConfirm) as unknown as { confirm: ReturnType<typeof vi.fn> };
    page['toggleGrant']({ code: 'AUDIT:READ' });
    facade.getPermissions.mockClear();

    confirm.confirm.mockResolvedValueOnce(false);
    await page['onAsOfPicked'](new Date(2026, 0, 1));
    expect(confirm.confirm).toHaveBeenCalledTimes(1);
    expect(page['asOf']()).toBe('');
    expect(page['permissionsDirty']()).toBe(true);
    expect(facade.getPermissions).not.toHaveBeenCalled();

    confirm.confirm.mockResolvedValueOnce(true);
    await page['onAsOfPicked'](new Date(2026, 0, 1));
    expect(page['asOf']()).toBe('2026-01-01');
    expect(page['permissionsDirty']()).toBe(false);

    // «Back to today» with no draft does not ask.
    confirm.confirm.mockClear();
    await page['onAsOfPicked'](null);
    expect(confirm.confirm).not.toHaveBeenCalled();
    expect(page['asOf']()).toBe('');
  });

  it('«Back to today» asks too when the draft is dirty', async () => {
    const { fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    const confirm = TestBed.inject(NxConfirm) as unknown as { confirm: ReturnType<typeof vi.fn> };
    await page['onAsOfPicked'](new Date(2026, 0, 1));
    page['asOf'].set('2026-01-01');
    page['originalCodes'].set(new Set());
    page['grantedCodes'].set(new Set(['AUDIT:READ'])); // a draft left over
    confirm.confirm.mockResolvedValueOnce(false);
    await page['onAsOfPicked'](null);
    expect(confirm.confirm).toHaveBeenCalled();
    expect(page['asOf']()).toBe('2026-01-01');
  });

  it('ignores a typed as-of date after today', async () => {
    const { facade, fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    facade.getPermissions.mockClear();
    await page['onAsOfPicked'](new Date(Date.now() + 3 * 86_400_000));
    expect(page['asOf']()).toBe('');
    expect(facade.getPermissions).not.toHaveBeenCalled();
  });

  it('the matrix checkboxes are disabled and inert while permissions are being saved', async () => {
    const { fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    const el = await openPermissionsTab(fixture);
    const input = () =>
      el.querySelector<HTMLInputElement>('mat-checkbox[data-testid="role-permission-cell-AUDIT:READ"] input');
    expect(input()?.disabled).toBe(false);

    page['savingPermissions'].set(true);
    fixture.detectChanges();
    expect(input()?.disabled).toBe(true);
    page['toggleGrant']({ code: 'AUDIT:READ' });
    expect(page['permissionsDirty']()).toBe(false);
  });

  it('does not call the server when nothing changed', async () => {
    const { facade, fixture } = await setupMatrix(['AUDIT:READ']);
    await fixture.componentInstance['onSavePermissions']();
    expect(facade.savePermissions).not.toHaveBeenCalled();
  });

  it('permission save: 400 -> validation text, 422 -> rule message, 409 CONCURRENCY -> nothing and keeps the draft', async () => {
    const { facade, fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    page['toggleGrant']({ code: 'AUDIT:READ' });

    facade.savePermissions.mockRejectedValueOnce({ status: 400, error: { errors: [{ field: 'permissionCodes', message: 'x' }] } });
    await page['onSavePermissions']();
    expect(page['permissionsError']()).toBe('Fix the fields');

    facade.savePermissions.mockRejectedValueOnce({ status: 422, error: { code: 'ACC-R9' } });
    await page['onSavePermissions']();
    expect(page['permissionsError']()).toBe('ACC-R9');

    facade.savePermissions.mockRejectedValueOnce({
      status: 409,
      error: { type: 'https://nexus.nussli.ch/problems/conflict', code: 'CONCURRENCY', status: 409 },
    });
    await page['onSavePermissions']();
    expect(page['permissionsError']()).toBeNull();
    expect(page['grantedCodes']().has('AUDIT:READ')).toBe(true);
  });

  it('a late permission response for a superseded asOf does not overwrite the newer one', async () => {
    const { facade, fixture } = await setupMatrix([]);
    const page = fixture.componentInstance;
    let resolveSlow!: (value: PermissionGrant[]) => void;
    facade.getPermissions.mockReturnValueOnce(new Promise<PermissionGrant[]>((resolve) => (resolveSlow = resolve)));
    facade.getPermissions.mockResolvedValueOnce([{ ...grant, code: 'AUDIT:READ' }]);

    const slow = page['onAsOfChange']('2026-01-01');
    await page['onAsOfChange']('2026-02-01');
    resolveSlow([{ ...grant, code: 'ORG_STRUCTURE:CREATE' }]);
    await slow;

    expect([...page['grantedCodes']()]).toEqual(['AUDIT:READ']);
    expect(page['loadingPermissions']()).toBe(false);
  });

  it('a system role keeps name/permissions editable but offers no deactivate', async () => {
    const facade = createFacadeStub({ ...role, isSystem: true });
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('[data-testid="role-detail-system-badge"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="role-detail-deactivate"]')).toBeNull();
    expect(fixture.componentInstance['canEditPermissions']()).toBe(true);
  });

  it('deactivates through the confirm dialog and not when it is cancelled', async () => {
    const facade = createFacadeStub(role);
    facade.deactivate.mockResolvedValue({ ...role, isActive: false });
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();
    const confirm = TestBed.inject(NxConfirm) as unknown as { confirm: ReturnType<typeof vi.fn> };

    confirm.confirm.mockResolvedValueOnce(false);
    await fixture.componentInstance['onDeactivate']();
    expect(facade.deactivate).not.toHaveBeenCalled();

    await fixture.componentInstance['onDeactivate']();
    expect(facade.deactivate).toHaveBeenCalledWith(role.id);
  });

  it('is read-only without ROLE:UPDATE', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ROLE:READ', scopes: [] }] as never);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance['canUpdate']()).toBe(false);
    expect(fixture.componentInstance['canEditPermissions']()).toBe(false);
  });
});
