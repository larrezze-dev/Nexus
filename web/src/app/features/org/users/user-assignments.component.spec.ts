import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import { NxConfirm } from '../../../shared/ui/layout';
import type { AssignmentErrorInfo, AssignmentRow } from '../../access/role-assignments/role-assignments.facade';
import { RoleAssignmentsFacade } from '../../access/role-assignments/role-assignments.facade';
import { UserAssignmentsComponent } from './user-assignments.component';

const role = { id: 'r-1', code: 'ORG_ADMIN', captionI18n: { de: 'Org-Admin' }, isActive: true, isSystem: false } as RoleListItem;
const row = (over: Partial<AssignmentRow> = {}): AssignmentRow => ({
  id: 'a-1',
  roleLabel: 'Org-Admin',
  scopeType: 'LEGAL_ENTITY',
  scopeLabel: 'Nüssli AG',
  validFromLabel: '1 Jan 2026',
  validToLabel: null,
  source: 'MANUAL',
  status: 'active',
  isActive: true,
  canRevoke: true,
  ...over,
});

function setup(rows: AssignmentRow[] = [row()]) {
  const facade = {
    rows: signal(rows),
    loading: signal(false),
    error: signal<string | null>(null),
    saving: signal(false),
    reset: vi.fn(),
    list: vi.fn().mockResolvedValue([]),
    create: vi.fn().mockResolvedValue({}),
    revoke: vi.fn().mockResolvedValue({}),
    describeError: vi.fn<(e: unknown) => AssignmentErrorInfo>().mockReturnValue({ kind: 'generic', message: 'generic', fields: {} }),
    searchRoles: () => of([role]),
    searchLegalEntities: () => of([]),
    searchBusinessUnits: () => of([]),
    searchWarehouses: () => of([]),
    displayRole: (r: RoleListItem) => r.code,
    displayLegalEntity: (e: { code: string }) => e.code,
    displayBusinessUnit: (e: { code: string }) => e.code,
    displayWarehouse: (e: { code: string }) => e.code,
  };
  const confirm = { confirm: vi.fn().mockResolvedValue(true) };
  TestBed.configureTestingModule({
    imports: [UserAssignmentsComponent],
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      provideNativeDateAdapter(),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: RoleAssignmentsFacade, useValue: facade },
      { provide: NxConfirm, useValue: confirm },
    ],
  });
  const fixture: ComponentFixture<UserAssignmentsComponent> = TestBed.createComponent(UserAssignmentsComponent);
  fixture.componentRef.setInput('userId', 'u-1');
  return { fixture, facade, confirm };
}

async function render(fixture: ComponentFixture<UserAssignmentsComponent>): Promise<HTMLElement> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}
const q = (root: HTMLElement, id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);

describe('UserAssignmentsComponent', () => {
  it('loads the assignments of the user and shows role, scope and dates by name', async () => {
    const { fixture, facade } = setup([row(), row({ id: 'a-2', scopeType: 'GLOBAL', scopeLabel: null, validToLabel: '31 Dec 2026' })]);
    const root = await render(fixture);

    expect(facade.list).toHaveBeenCalledWith('u-1');
    const items = root.querySelectorAll('[data-testid="assignments-list"] tbody tr');
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('Org-Admin');
    // Scope reads «type · name»; GLOBAL has no name; validity «from – to» or «from – no end».
    expect(q(items[0] as HTMLElement, 'assignment-scope')?.textContent).toBe(
      'org.users.detail.assignments.scopeTypes.LEGAL_ENTITY · Nüssli AG',
    );
    expect(q(items[1] as HTMLElement, 'assignment-scope')?.textContent).toBe('org.users.detail.assignments.scopeTypes.GLOBAL');
    expect(q(items[0] as HTMLElement, 'assignment-validity')?.textContent).toContain('1 Jan 2026 –');
    expect(q(items[0] as HTMLElement, 'assignment-validity')?.textContent).toContain('org.users.detail.assignments.openEnd');
    expect(q(items[1] as HTMLElement, 'assignment-validity')?.textContent).toContain('31 Dec 2026');
    expect(q(items[0] as HTMLElement, 'assignment-source')?.textContent).toContain('org.users.detail.assignments.sources.MANUAL');
    expect(items[0].querySelector('nx-status-badge')).not.toBeNull();
    // Column headers are translated keys, the actions column is named for screen readers.
    expect(root.querySelectorAll('[data-testid="assignments-list"] th')).toHaveLength(6);
    expect(q(root, 'effective-scope-link')?.getAttribute('href')).toBe('/access/users/u-1/effective-scope');
  });

  it('shows the empty state, and the load error', async () => {
    const { fixture, facade } = setup([]);
    const root = await render(fixture);
    expect(q(root, 'assignments-empty')).not.toBeNull();

    facade.error.set('Could not load');
    fixture.detectChanges();
    expect(q(root, 'assignments-load-error')?.textContent).toContain('Could not load');
  });

  it('revoke asks for confirmation first and refreshes the list', async () => {
    const { fixture, facade, confirm } = setup();
    const root = await render(fixture);

    confirm.confirm.mockResolvedValueOnce(false);
    q(root, 'assignment-revoke-a-1')?.click();
    await fixture.whenStable();
    expect(facade.revoke).not.toHaveBeenCalled();

    q(root, 'assignment-revoke-a-1')?.click();
    await fixture.whenStable();
    expect(confirm.confirm).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
    expect(facade.revoke).toHaveBeenCalledWith('a-1');
    expect(facade.list).toHaveBeenCalledTimes(2);
  });

  it('rows that cannot be revoked have no revoke button', async () => {
    const { fixture } = setup([row({ id: 'seed', source: 'SEED', canRevoke: false })]);
    expect(q(await render(fixture), 'assignment-revoke-seed')).toBeNull();
  });

  it('grant: invalid form is not sent; a valid one sends Date-derived instants and reloads', async () => {
    const { fixture, facade } = setup([]);
    await render(fixture);
    const cmp = fixture.componentInstance as unknown as {
      form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm>;
      grant(): Promise<void>;
    };

    await cmp.grant();
    expect(facade.create).not.toHaveBeenCalled();

    cmp.form.controls.role.setValue(role);
    cmp.form.controls.scopeEntity.setValue({ id: 'le-1', label: 'LE' });
    cmp.form.controls.validFrom.setValue(new Date(2026, 10, 14));
    cmp.form.controls.validTo.setValue(new Date(2026, 10, 14));
    await cmp.grant();

    expect(facade.create).toHaveBeenCalledWith({
      roleId: 'r-1',
      userId: 'u-1',
      scopeType: 'LEGAL_ENTITY',
      scopeEntityId: 'le-1',
      validFrom: '2026-11-14T00:00:00.000Z',
      validTo: '2026-11-15T00:00:00.000Z',
    });
    expect(facade.list).toHaveBeenCalledTimes(2);
    expect(cmp.form.controls.role.value).toBeNull();
  });

  it('granting one that starts in the future shows a persistent notice with the date; a current one does not', async () => {
    const { fixture, facade } = setup([]);
    const root = await render(fixture);
    const cmp = fixture.componentInstance as unknown as {
      form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm>;
      grant(): Promise<void>;
    };
    const fill = (from: Date) => {
      cmp.form.controls.role.setValue(role);
      cmp.form.controls.scopeEntity.setValue({ id: 'le-1', label: 'LE' });
      cmp.form.controls.validFrom.setValue(from);
    };

    fill(new Date(Date.now() + 40 * 86_400_000));
    await cmp.grant();
    fixture.detectChanges();
    const notice = q(root, 'assignment-future-notice');
    expect(facade.create).toHaveBeenCalledTimes(1);
    expect(notice?.textContent).toContain('org.users.detail.assignments.startsOn');
    expect(notice?.getAttribute('role')).toBe('status');

    fill(new Date(2020, 0, 1));
    await cmp.grant();
    fixture.detectChanges();
    expect(q(root, 'assignment-future-notice')).toBeNull();
  });

  it('grant error ACC-R2: the message goes under the scope entity and the control is marked', async () => {
    const { fixture, facade } = setup([]);
    const root = await render(fixture);
    const cmp = fixture.componentInstance as unknown as { form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm>; grant(): Promise<void> };
    facade.create.mockRejectedValue(new Error('http'));
    facade.describeError.mockReturnValue({ kind: 'rule', message: null, fields: { scopeEntity: ['errors.ACC-R2'] } });
    cmp.form.controls.role.setValue(role);
    cmp.form.controls.scopeEntity.setValue({ id: 'le-1', label: 'LE' });
    cmp.form.controls.validFrom.setValue(new Date(2026, 10, 14));

    await cmp.grant();
    fixture.detectChanges();

    expect(
      root.querySelector('[data-testid="assignment-scope-entity-picker"] [data-testid="nx-entity-picker-error"]')?.textContent,
    ).toContain('errors.ACC-R2');
    expect(cmp.form.controls.scopeEntity.hasError('server')).toBe(true);
  });

  it('409 CONCURRENCY on grant shows nothing', async () => {
    const { fixture, facade } = setup([]);
    const root = await render(fixture);
    const cmp = fixture.componentInstance as unknown as { form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm>; grant(): Promise<void> };
    facade.create.mockRejectedValue(new Error('http'));
    facade.describeError.mockReturnValue({ kind: 'concurrency', message: null, fields: {} });
    cmp.form.controls.role.setValue(role);
    cmp.form.controls.scopeEntity.setValue({ id: 'le-1', label: 'LE' });
    cmp.form.controls.validFrom.setValue(new Date(2026, 10, 14));

    await cmp.grant();
    fixture.detectChanges();

    expect(q(root, 'assignment-save-error')).toBeNull();
  });

  it('shows the until-before-from error under the date', async () => {
    const { fixture } = setup([]);
    const root = await render(fixture);
    const cmp = fixture.componentInstance as unknown as { form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm> };
    cmp.form.controls.validFrom.setValue(new Date(2026, 10, 14));
    cmp.form.controls.validTo.setValue(new Date(2026, 10, 1));
    fixture.detectChanges();

    expect(q(root, 'assignment-date-range-error')).not.toBeNull();
  });

  it('invalid submit: alert with the number of fields, and the required texts under the pickers', async () => {
    const { fixture } = setup([]);
    const root = await render(fixture);

    q(root, 'assignment-grant-button')?.click();
    fixture.detectChanges();

    expect(q(root, 'assignment-form-invalid')?.textContent).toContain('shared.form.reviewFields');
    expect(
      root.querySelector('[data-testid="assignment-role-picker"] [data-testid="nx-entity-picker-error"]')?.textContent,
    ).toContain('shared.form.required');
    expect(q(root, 'assignment-valid-from-error')).not.toBeNull();
  });

  it('GLOBAL: the entity field stays visible but disabled, with a hint', async () => {
    const { fixture } = setup([]);
    const root = await render(fixture);
    const cmp = fixture.componentInstance as unknown as { form: ReturnType<typeof import('../../access/role-assignments/role-assignment-form').createRoleAssignmentForm> };

    cmp.form.controls.scopeType.setValue('GLOBAL');
    fixture.detectChanges();

    const input = root.querySelector<HTMLInputElement>('[data-testid="assignment-scope-entity-picker"] input');
    expect(input?.disabled).toBe(true);
    expect(root.querySelector('[data-testid="assignment-scope-entity-picker"] mat-hint')?.textContent).toContain(
      'org.users.detail.assignments.hints.global',
    );
  });

  it('read-only (SYSTEM user): no grant form and no revoke', async () => {
    const { fixture } = setup();
    fixture.componentRef.setInput('readOnly', true);
    const root = await render(fixture);

    expect(q(root, 'assignment-grant-form')).toBeNull();
    expect(q(root, 'assignment-revoke-a-1')).toBeNull();
    expect(q(root, 'effective-scope-link')).not.toBeNull();
  });
});
