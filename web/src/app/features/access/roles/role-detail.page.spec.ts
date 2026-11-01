import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { PermissionGrant } from '../../../api/platform/models/permission-grant';
import { Role } from '../../../api/platform/models/role';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { RoleDetailPage } from './role-detail.page';
import { RolesFacade } from './roles.facade';

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
        permissionMatrix: { asOf: 'As of', resource: 'Resource' },
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
  },
};

function createFacadeStub(initialSelected: Role | null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    loading: signal(false).asReadonly(),
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
      { provide: RolesFacade, useValue: facade },
      { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null | undefined) => code ?? 'generic' } },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
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
    facade.create.mockRejectedValue({ status: 400, error: { errors: { code: ['Code already in use'] } } });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    fixture.componentInstance['form'].controls.code.setValue('ADMIN');
    fixture.componentInstance['form'].controls.captionI18n.setValue({ de: 'Admin' });

    await fixture.componentInstance['onSave']();

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['Code already in use'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
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
    fixture.componentInstance['toggleGrant']({
      code: grant.code,
      action: 'CREATE',
      resourceType: 'ORG_STRUCTURE',
      captionI18n: { de: 'Anlegen' },
    });

    await fixture.componentInstance['onSavePermissions']();

    expect(facade.savePermissions).toHaveBeenCalledWith(role.id, [grant.code]);
  });

  it('makes the permission matrix read-only once a past asOf date is picked', async () => {
    const facade = createFacadeStub(role);
    const fixture = await setupPage({ id: role.id }, facade);
    fixture.detectChanges();
    await fixture.whenStable();

    await fixture.componentInstance['onAsOfChange']('2026-01-01');

    expect(fixture.componentInstance['readOnlyMatrix']()).toBe(true);
    expect(fixture.componentInstance['canEditPermissions']()).toBe(false);
    expect(facade.getPermissions).toHaveBeenCalledWith(role.id, new Date('2026-01-01').toISOString());
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
});
