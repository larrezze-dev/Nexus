import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { RoleAssignmentsFacade } from '../../access/role-assignments/role-assignments.facade';
import { UserDetailPage } from './user-detail.page';
import { SYSTEM_USER_ID, UsersFacade } from './users.facade';
import { auditTrailInstances, clickTab, denyAuditRead, findAuditTab, selectAuditTab, stubAuditTrail } from '../audit-tab.testing';

/**
 * `MatTabGroup` only renders a tab's content once it has been activated at least once (it is
 * not an `*ngIf`-style conditional on the data, it's `mat-tab-body`'s own lazy-portal
 * behaviour) — "Asignaciones" is never the default-selected tab, so tests asserting on its
 * content must select it first. `ngOnInit`'s chained `await`s (`facade.load()`, optionally
 * `describeBusinessUnit()`, then `assignmentsFacade.list()`) also don't reliably settle within
 * a single `fixture.whenStable()` wait once a third `await` is in the chain in this test
 * environment, so this polls microtask/macrotask turns until one of the tab's terminal states
 * (the empty-state marker or the rendered list) shows up in the DOM, instead of guessing how
 * many turns the chain needs.
 */
async function flushUntilAssignmentsLoaded(fixture: ComponentFixture<UserDetailPage>): Promise<void> {
  const root = fixture.nativeElement as HTMLElement;
  const tabs = root.querySelectorAll<HTMLElement>('[role="tab"]');
  tabs[1]?.click();
  fixture.detectChanges();

  for (let i = 0; i < 20; i++) {
    if (root.querySelector('[data-testid="assignments-empty"], [data-testid="assignments-list"]')) {
      return;
    }
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }
}

function setup(
  paramMap: Record<string, string>,
  facadeOverrides: Partial<Record<string, unknown>> = {},
  assignmentsFacadeOverrides: Partial<Record<string, unknown>> = {},
) {
  const facade = {
    selected: signal(null),
    loading: signal(false),
    saving: signal(false),
    error: signal(null),
    fieldErrors: signal(null),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn().mockResolvedValue(null),
    update: vi.fn().mockResolvedValue(null),
    deactivate: vi.fn().mockResolvedValue(null),
    reset: vi.fn(),
    searchBusinessUnits: vi.fn(),
    formatBusinessUnit: (bu: { code: string; captionI18n: { de: string } }) => `${bu.code} — ${bu.captionI18n.de}`,
    describeBusinessUnit: vi.fn().mockResolvedValue('BU-01 — Operations'),
    ...facadeOverrides,
  };

  const assignmentsFacade = {
    loading: signal(false),
    list: vi.fn().mockResolvedValue([]),
    canRevoke: vi.fn().mockReturnValue(false),
    create: vi.fn(),
    revoke: vi.fn(),
    getEffectiveScope: vi.fn(),
    searchRoles: vi.fn(),
    searchLegalEntities: vi.fn(),
    searchBusinessUnits: vi.fn(),
    searchWarehouses: vi.fn(),
    ...assignmentsFacadeOverrides,
  };

  TestBed.configureTestingModule({
    imports: [UserDetailPage],
    providers: [
      provideNoopAnimations(),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: UsersFacade, useValue: facade },
      { provide: RoleAssignmentsFacade, useValue: assignmentsFacade },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
    ],
  });

  stubAuditTrail(UserDetailPage);
  const fixture = TestBed.createComponent(UserDetailPage);
  return { fixture, facade, assignmentsFacade };
}

describe('UserDetailPage', () => {
  it('is in "new" mode without an id param and does not call load()', async () => {
    const { fixture, facade } = setup({});
    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.load).not.toHaveBeenCalled();
    expect(facade.reset).toHaveBeenCalled();
  });

  it('loads an existing user and populates the form', async () => {
    const user = {
      id: 'u-1',
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      oid: 'oid-1',
      preferredLanguage: 'en' as const,
      businessUnitId: null,
      partyId: 'p-1',
      isActive: true,
    };
    const { fixture, facade } = setup(
      { id: 'u-1' },
      { selected: signal(user), load: vi.fn().mockImplementation(async () => {}) },
    );
    // `load()` is a no-op mock, so manually point `selected` before `ngOnInit` reads it.
    (facade.load as ReturnType<typeof vi.fn>).mockImplementation(async () => undefined);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.load).toHaveBeenCalledWith('u-1');
    expect(fixture.componentInstance['form'].controls.fullName.value).toBe('Jane Doe');
  });

  it('hides the deactivate action and disables the form for the SYSTEM user (ORG-R6)', async () => {
    const systemUser = {
      id: SYSTEM_USER_ID,
      fullName: 'SYSTEM',
      email: null,
      oid: null,
      preferredLanguage: 'de' as const,
      businessUnitId: null,
      partyId: 'p-system',
      isActive: true,
    };
    const { fixture } = setup(
      { id: SYSTEM_USER_ID },
      { selected: signal(systemUser) },
    );

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance['isSystemUser']()).toBe(true);
    expect(fixture.componentInstance['form'].disabled).toBe(true);
    const deactivateButton = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="user-deactivate-button"]',
    );
    expect(deactivateButton).toBeNull();
  });

  it('shows the ORG-R9 email field error returned by the facade', async () => {
    const { fixture, facade } = setup({}, { fieldErrors: signal({ email: 'A user with this email already exists.' }) });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="user-email-error"]');
    expect(error?.textContent).toContain('already exists');
    expect(facade.create).not.toHaveBeenCalled();
  });

  it('does not render the "Asignaciones" tab for a brand-new (not-yet-saved) user', async () => {
    const { fixture } = setup({});
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance['isNew']()).toBe(true);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('org.users.detail.tabs.assignments');
  });

  it('loads the user\'s role assignments once it exists and shows the empty state', async () => {
    const user = {
      id: 'u-1',
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      oid: 'oid-1',
      preferredLanguage: 'en' as const,
      businessUnitId: null,
      partyId: 'p-1',
      isActive: true,
    };
    const { fixture, assignmentsFacade } = setup({ id: 'u-1' }, { selected: signal(user) });

    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    expect(assignmentsFacade.list).toHaveBeenCalledWith('u-1');
    const empty = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="assignments-empty"]');
    expect(empty).not.toBeNull();
  });

  it('only offers "Revocar" on rows the facade knows an ETag for (contract gap)', async () => {
    const user = {
      id: 'u-1',
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      oid: 'oid-1',
      preferredLanguage: 'en' as const,
      businessUnitId: null,
      partyId: 'p-1',
      isActive: true,
    };
    const assignment = {
      id: 'ra-1',
      roleId: 'role-1',
      userId: 'u-1',
      scopeType: 'LEGAL_ENTITY' as const,
      scopeEntityId: 'le-1',
      source: 'SEED' as const,
      validFrom: '2026-01-01T00:00:00Z',
      validTo: null,
    };
    const { fixture, assignmentsFacade } = setup(
      { id: 'u-1' },
      { selected: signal(user) },
      { list: vi.fn().mockResolvedValue([assignment]), canRevoke: vi.fn().mockReturnValue(false) },
    );

    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    const revokeButton = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="assignment-revoke-ra-1"]');
    expect(revokeButton).toBeNull();
  });

  const existingUser = {
    id: 'u-1',
    fullName: 'Jane Doe',
    email: 'jane@example.com',
    oid: 'oid-1',
    preferredLanguage: 'en' as const,
    businessUnitId: null,
    partyId: 'p-1',
    isActive: true,
  };

  it('disables the grant submit button until a role and a scope entity are both picked', async () => {
    const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    const root = fixture.nativeElement as HTMLElement;
    const getButton = () => root.querySelector<HTMLButtonElement>('[data-testid="assignment-grant-button"]');

    expect(getButton()?.disabled).toBe(true);

    fixture.componentInstance['onRoleSelected']({
      id: 'role-1',
      code: 'ADMIN',
      captionI18n: { de: 'Admin' },
      isSystem: false,
      isActive: true,
    });
    fixture.detectChanges();
    // `scopeType` defaults to `LEGAL_ENTITY`, which still needs an entity picked.
    expect(getButton()?.disabled).toBe(true);

    fixture.componentInstance['onLegalEntityScopeSelected']({
      id: 'le-1',
      code: 'LE-CH',
      legalName: 'SAPE smart bud AG',
      countryCode: 'CH',
      currencyCode: 'CHF',
      groupId: 'g-1',
      isActive: true,
    });
    fixture.detectChanges();
    expect(getButton()?.disabled).toBe(false);
  });

  it('does not disable the submit button on GLOBAL scope once only a role is picked', async () => {
    const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    fixture.componentInstance['grantForm'].controls.scopeType.setValue('GLOBAL');
    fixture.componentInstance['onRoleSelected']({
      id: 'role-1',
      code: 'ADMIN',
      captionI18n: { de: 'Admin' },
      isSystem: false,
      isActive: true,
    });
    fixture.detectChanges();

    const button = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-testid="assignment-grant-button"]',
    );
    expect(button?.disabled).toBe(false);
  });

  it('shows a translated 422 business-rule error and does not reset the grant form when create() rejects', async () => {
    const createError = { status: 422, error: { code: 'ACC-R1', title: 'Business rule violated' } };
    const { fixture, assignmentsFacade } = setup(
      { id: 'u-1' },
      { selected: signal(existingUser) },
      { create: vi.fn().mockRejectedValue(createError) },
    );
    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    fixture.componentInstance['grantForm'].controls.scopeType.setValue('GLOBAL');
    fixture.componentInstance['grantForm'].controls.validFrom.setValue('2026-01-01');
    fixture.componentInstance['onRoleSelected']({
      id: 'role-1',
      code: 'ADMIN',
      captionI18n: { de: 'Admin' },
      isSystem: false,
      isActive: true,
    });

    await fixture.componentInstance['grantAssignment']();
    fixture.detectChanges();

    expect(assignmentsFacade.create).toHaveBeenCalled();
    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="assignment-save-error"]');
    expect(error).not.toBeNull();
    expect(error?.textContent?.trim().length).toBeGreaterThan(0);
    // The role picker still shows the previously-picked role: a failed grant doesn't silently
    // reset the form the user just filled in.
    expect(fixture.componentInstance['roleId']()).toBe('role-1');
  });

  it('shows per-field 400 errors and the validation summary when create() rejects with a 400', async () => {
    const createError = {
      status: 400,
      error: { errors: { validFrom: ['validFrom must be before validTo'] } },
    };
    const { fixture, assignmentsFacade } = setup(
      { id: 'u-1' },
      { selected: signal(existingUser) },
      { create: vi.fn().mockRejectedValue(createError) },
    );
    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    fixture.componentInstance['grantForm'].controls.scopeType.setValue('GLOBAL');
    fixture.componentInstance['grantForm'].controls.validFrom.setValue('2026-01-01');
    fixture.componentInstance['onRoleSelected']({
      id: 'role-1',
      code: 'ADMIN',
      captionI18n: { de: 'Admin' },
      isSystem: false,
      isActive: true,
    });

    await fixture.componentInstance['grantAssignment']();
    fixture.detectChanges();

    expect(assignmentsFacade.create).toHaveBeenCalled();
    const fieldError = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="assignment-valid-from-error"]');
    expect(fieldError?.textContent).toContain('validFrom must be before validTo');
  });

  it('surfaces a revoke() failure instead of leaving an unhandled rejection', async () => {
    const assignment = {
      id: 'ra-1',
      roleId: 'role-1',
      userId: 'u-1',
      scopeType: 'LEGAL_ENTITY' as const,
      scopeEntityId: 'le-1',
      source: 'SEED' as const,
      validFrom: '2026-01-01T00:00:00Z',
      validTo: null,
    };
    const revokeError = { status: 422, error: { code: 'ACC-R2' } };
    const { fixture, assignmentsFacade } = setup(
      { id: 'u-1' },
      { selected: signal(existingUser) },
      {
        list: vi.fn().mockResolvedValue([assignment]),
        canRevoke: vi.fn().mockReturnValue(true),
        revoke: vi.fn().mockRejectedValue(revokeError),
      },
    );
    fixture.detectChanges();
    await flushUntilAssignmentsLoaded(fixture);

    await expect(fixture.componentInstance['revokeAssignment']('ra-1')).resolves.toBeUndefined();
    fixture.detectChanges();

    expect(assignmentsFacade.revoke).toHaveBeenCalledWith('ra-1');
    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="assignment-save-error"]');
    expect(error).not.toBeNull();
  });

  describe('audit tab', () => {
    it('shows an "Auditoría" tab for an existing record and renders nx-audit-trail when selected', async () => {
      const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const audit = await selectAuditTab(fixture);

      expect(audit).not.toBeNull();
      expect(audit?.aggregateType()).toBe('user');
      expect(audit?.aggregateId()).toBe('u-1');
    });

    it('has no "Auditoría" tab when creating a new record', async () => {
      const { fixture } = setup({});
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(findAuditTab(fixture)).toBeUndefined();
    });

    it('has no "Auditoría" tab without AUDIT:READ', async () => {
      const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
      denyAuditRead();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(findAuditTab(fixture)).toBeUndefined();
    });

    it('calls facade.reset() before facade.load() in edit mode', async () => {
      const { fixture, facade } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
      fixture.detectChanges();
      await fixture.whenStable();
      const reset = facade.reset as ReturnType<typeof vi.fn>;
      const load = facade.load as ReturnType<typeof vi.fn>;
      expect(reset.mock.invocationCallOrder[0]).toBeLessThan(load.mock.invocationCallOrder[0]);
    });

    it('does not show the audit tab nor a foreign id when selected is another user (failed load)', async () => {
      const { fixture } = setup({ id: 'u-2' }, { selected: signal(existingUser) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(findAuditTab(fixture)).toBeUndefined();
      expect(auditTrailInstances(fixture)).toHaveLength(0);
    });

    it('re-creates nx-audit-trail every time the audit tab is re-selected', async () => {
      const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const first = await selectAuditTab(fixture);
      await clickTab(fixture, 'org.users.detail.tabs.general');
      await clickTab(fixture, 'common.tabs.audit');
      const second = await selectAuditTab(fixture);

      expect(first).not.toBeNull();
      expect(second).not.toBeNull();
      expect(second).not.toBe(first);
    });

    it('keeps the General form values across General -> Auditoría -> General', async () => {
      const { fixture } = setup({ id: 'u-1' }, { selected: signal(existingUser) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const input = () =>
        (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-testid="user-full-name-input"]');
      input()!.value = 'Edited Name';
      input()!.dispatchEvent(new Event('input'));
      await selectAuditTab(fixture);
      await clickTab(fixture, 'org.users.detail.tabs.general');

      expect(input()?.value).toBe('Edited Name');
      expect(fixture.componentInstance['form'].controls.fullName.value).toBe('Edited Name');
    });

    it('shows neither the "Asignaciones" nor the "Auditoría" tab when the user failed to load', async () => {
      const { fixture } = setup({ id: 'u-1' }, { selected: signal(null) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const tabs = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]'));
      expect(tabs).toHaveLength(1);
      expect(findAuditTab(fixture)).toBeUndefined();
    });

    it('does not request the assignments list when the user failed to load', async () => {
      const { fixture, assignmentsFacade } = setup({ id: 'u-1' }, { selected: signal(null) });
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(assignmentsFacade.list).not.toHaveBeenCalled();
    });
  });
});
