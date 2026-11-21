import { Component, input, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { NxFormatService } from '../../../core/i18n/nx-format.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { stubAuditTrail } from '../audit-tab.testing';
import { UserAssignmentsComponent } from './user-assignments.component';
import { UserDetailPage } from './user-detail.page';
import { SYSTEM_USER_ID, SystemUserReadOnlyError } from '../../../shared/auth/system-user';
import { UsersFacade, type UserErrorInfo } from './users.facade';

@Component({ selector: 'nx-user-assignments', template: '<p data-testid="assignments-stub"></p>' })
class UserAssignmentsStubComponent {
  readonly userId = input.required<string>();
  readonly readOnly = input(false);
}

const user = {
  updatedAt: '2026-11-11T10:00:00Z',
  id: 'u-1',
  fullName: 'Jane Doe',
  email: 'jane@example.com',
  oid: 'oid-1',
  preferredLanguage: 'en' as const,
  businessUnitId: null,
  partyId: 'p-1',
  isActive: true,
};

function setup(paramMap: Record<string, string>, selected: object | null = null, overrides: Record<string, unknown> = {}) {
  const selectedSignal = signal<object | null>(selected);
  const facade = {
    selected: selectedSignal,
    loading: signal(false),
    saving: signal(false),
    error: signal<string | null>(null),
    title: signal<string | null>((selected as { fullName?: string } | null)?.fullName ?? null),
    isSystem: signal((selected as { id?: string } | null)?.id === SYSTEM_USER_ID),
    businessUnitRef: signal(null),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn().mockResolvedValue({ ...user, id: 'u-new' }),
    update: vi.fn().mockResolvedValue(user),
    deactivate: vi.fn().mockResolvedValue({ ...user, isActive: false }),
    reset: vi.fn(),
    describeError: vi.fn<(error: unknown) => UserErrorInfo>().mockReturnValue({ kind: 'generic', message: 'generic text', fields: {} }),
    searchBusinessUnits: vi.fn(),
    formatBusinessUnit: (bu: { code: string }) => bu.code,
    ...overrides,
  };
  const breadcrumb = {
    setCurrentLabel: vi.fn(),
    setLoadFailed: vi.fn(),
    clearLoadFailed: vi.fn(),
    claimForPage: () => () => undefined,
    trail: signal([]),
  };
  const confirm = { confirm: vi.fn().mockResolvedValue(true) };

  TestBed.configureTestingModule({
    imports: [UserDetailPage],
    providers: [
      provideNoopAnimations(),
      provideRouter([]),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: UsersFacade, useValue: facade },
      { provide: NxBreadcrumbService, useValue: breadcrumb },
      { provide: NxConfirm, useValue: confirm },
      { provide: NxFormatService, useValue: { formatDateMedium: () => '11.11.2026' } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
    ],
  });
  stubAuditTrail(UserDetailPage);
  TestBed.overrideComponent(UserDetailPage, {
    remove: { imports: [UserAssignmentsComponent] },
    add: { imports: [UserAssignmentsStubComponent] },
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(UserDetailPage);
  return { fixture, facade, breadcrumb, confirm, navigate, selectedSignal };
}

async function render(fixture: ComponentFixture<UserDetailPage>): Promise<HTMLElement> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const q = (root: HTMLElement, testId: string) => root.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
const type = (root: HTMLElement, testId: string, value: string) => {
  const input = q(root, testId) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
};

describe('UserDetailPage', () => {
  it('new mode: does not load, requires oid and full name, and creates then opens the new user', async () => {
    const { fixture, facade, navigate } = setup({});
    const root = await render(fixture);

    expect(facade.load).not.toHaveBeenCalled();
    // Create template: no tabs, header «New user», «Create user» at the bottom.
    expect(root.querySelector('[role="tab"]')).toBeNull();
    expect(root.querySelector('nx-page-header h1')?.textContent).toContain('org.users.detail.newTitle');
    expect(q(root, 'user-save-button')?.textContent).toContain('org.users.actions.create');
    // Sign-up: the required Microsoft Entra ID is visible in the sign-in section, no «Advanced» fold.
    expect(q(root, 'user-advanced-section')).toBeNull();
    expect(q(root, 'user-oid-input')?.closest('[hidden]')).toBeNull();
    expect(root.textContent).toContain('org.users.sections.access');

    q(root, 'user-save-button')?.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(facade.create).not.toHaveBeenCalled();
    // Invalid submit: alert with the number of fields to review, focus on the first one.
    expect(q(root, 'user-form-invalid')?.textContent).toContain('shared.form.reviewFields');
    await new Promise((resolve) => setTimeout(resolve));
    expect(document.activeElement).toBe(q(root, 'user-full-name-input'));

    type(root, 'user-oid-input', 'oid-9');
    type(root, 'user-full-name-input', 'New Person');
    q(root, 'user-save-button')?.click();
    await fixture.whenStable();

    expect(facade.create).toHaveBeenCalledWith(expect.objectContaining({ oid: 'oid-9', fullName: 'New Person', businessUnitId: null }));
    expect(navigate).toHaveBeenCalled();
  });

  it('edit mode: loads, fills the form and names the breadcrumb with the user', async () => {
    const { fixture, facade, breadcrumb } = setup({ id: 'u-1' }, user);
    const root = await render(fixture);

    expect(facade.load).toHaveBeenCalledWith('u-1');
    expect((q(root, 'user-full-name-input') as HTMLInputElement).value).toBe('Jane Doe');
    expect(breadcrumb.setCurrentLabel).toHaveBeenCalledWith('Jane Doe');
    // Detail header: name as title, email · modified date, status badge.
    expect(root.querySelector('nx-page-header h1')?.textContent).toContain('Jane Doe');
    expect(root.querySelector('.nx-page-header__subtitle')?.textContent).toContain('jane@example.com · org.users.detail.modified');
    expect(root.querySelector('nx-page-header nx-status-badge')).not.toBeNull();
    expect(q(root, 'user-save-button')?.hasAttribute('disabled')).toBe(true); // nothing changed yet
  });

  it('edit mode: the Microsoft Entra ID sits in the folded «Advanced» section, closed until opened', async () => {
    const { fixture } = setup({ id: 'u-1' }, user);
    const root = await render(fixture);
    const advanced = q(root, 'user-advanced-section')!;
    const toggle = advanced.querySelector<HTMLButtonElement>('h2 button')!;
    const oid = q(root, 'user-oid-input') as HTMLInputElement;

    expect(toggle.textContent).toContain('org.users.sections.advanced');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(advanced.contains(oid)).toBe(true);
    expect(oid.closest('[hidden]')).not.toBeNull();
    expect(oid.value).toBe('oid-1');
    expect(oid.classList).toContain('nx-mono');
    // The sign-in section becomes «Organisation» (only the business unit is left in it).
    expect(root.textContent).toContain('org.users.sections.organisation');

    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(oid.closest('[hidden]')).toBeNull();
  });

  it('shows the skeleton while loading', async () => {
    const { fixture } = setup({ id: 'u-1' }, null);
    const root = await render(fixture);
    expect(root.querySelector('nx-detail-skeleton')).not.toBeNull();
  });

  it('a failed load shows the error with Retry, marks the breadcrumb and Retry loads again', async () => {
    const { fixture, facade, breadcrumb } = setup({ id: 'u-1' }, null);
    facade.error.set('Could not load');
    const root = await render(fixture);

    expect(q(root, 'user-load-error')).not.toBeNull();
    expect(breadcrumb.setLoadFailed).toHaveBeenCalled();

    q(root, 'user-retry-button')?.click();
    await fixture.whenStable();
    expect(facade.load).toHaveBeenCalledTimes(2);
    expect(breadcrumb.clearLoadFailed).toHaveBeenCalled();
  });

  describe('ORG-R6: SYSTEM user is read-only for real', () => {
    const system = { ...user, id: SYSTEM_USER_ID, fullName: 'SYSTEM', oid: null, email: null };

    it('no save, no deactivate, no cancel; form disabled; notice shown', async () => {
      const { fixture } = setup({ id: SYSTEM_USER_ID }, system);
      const root = await render(fixture);

      expect(q(root, 'user-system-notice')).not.toBeNull();
      expect(q(root, 'user-save-button')).toBeNull();
      expect(q(root, 'user-cancel-button')).toBeNull();
      expect(q(root, 'user-deactivate-button')).toBeNull();
      expect(root.querySelector('form')?.classList).toContain('nx-readonly');
      expect(q(root, 'nx-entity-picker-clear')).toBeNull();
      expect((q(root, 'user-full-name-input') as HTMLInputElement).disabled).toBe(true);
      expect(fixture.componentInstance['form'].disabled).toBe(true);
    });

    it('save() does nothing even if called', async () => {
      const { fixture, facade } = setup({ id: SYSTEM_USER_ID }, system);
      await render(fixture);

      await fixture.componentInstance['save']();
      await fixture.componentInstance['deactivate']();

      expect(facade.update).not.toHaveBeenCalled();
      expect(facade.deactivate).not.toHaveBeenCalled();
    });

    it('a refusal from the facade shows the rule text', async () => {
      const { fixture, facade } = setup({ id: 'u-1' }, user);
      facade.update.mockRejectedValue(new SystemUserReadOnlyError());
      facade.describeError.mockReturnValue({ kind: 'rule', message: 'errors.ORG-R6', fields: {} });
      const root = await render(fixture);

      type(root, 'user-full-name-input', 'Jane Q. Doe');
      fixture.detectChanges();
      q(root, 'user-save-button')?.click();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(q(root, 'user-save-error')?.textContent).toContain('errors.ORG-R6');
    });
  });

  describe('write errors', () => {
    async function failSave(info: UserErrorInfo) {
      const ctx = setup({ id: 'u-1' }, user);
      ctx.facade.update.mockRejectedValue(new Error('http'));
      ctx.facade.describeError.mockReturnValue(info);
      const root = await render(ctx.fixture);
      type(root, 'user-email-input', 'jane.doe@example.com');
      ctx.fixture.detectChanges();
      q(root, 'user-save-button')?.click();
      await ctx.fixture.whenStable();
      ctx.fixture.detectChanges();
      return { ...ctx, root };
    }

    it('409 CONCURRENCY: shows nothing (the interceptor dialog already ran)', async () => {
      const { root } = await failSave({ kind: 'concurrency', message: null, fields: {} });
      expect(q(root, 'user-save-error')).toBeNull();
      expect(q(root, 'user-oid-error')).toBeNull();
    });

    it('409 CONFLICT: the message and the marked field are on the oid', async () => {
      const { root, fixture } = await failSave({ kind: 'duplicate', message: null, fields: { oid: ['oid taken'] } });
      expect(q(root, 'user-oid-error')?.textContent).toContain('oid taken');
      expect(fixture.componentInstance['form'].controls.oid.hasError('server')).toBe(true);
      // The folded «Advanced» section opens so the marked field is visible.
      expect(q(root, 'user-advanced-section')?.querySelector('h2 button')?.getAttribute('aria-expanded')).toBe('true');
      expect(q(root, 'user-oid-input')?.closest('[hidden]')).toBeNull();
    });

    it('an error on another field leaves «Advanced» closed', async () => {
      const { root } = await failSave({ kind: 'rule', message: null, fields: { email: ['email taken'] } });
      expect(q(root, 'user-advanced-section')?.querySelector('h2 button')?.getAttribute('aria-expanded')).toBe('false');
    });

    it('422 ORG-R9: the message is under email', async () => {
      const { root } = await failSave({ kind: 'rule', message: null, fields: { email: ['email taken'] } });
      expect(q(root, 'user-email-error')?.textContent).toContain('email taken');
    });

    it('400: errors.validation above and the named field marked', async () => {
      const { root, fixture } = await failSave({
        kind: 'validation',
        message: 'Please fix the highlighted fields.',
        fields: { fullName: ['Invalid value'] },
      });
      expect(q(root, 'user-save-error')?.textContent).toContain('Please fix the highlighted fields.');
      expect(fixture.componentInstance['form'].controls.fullName.hasError('server')).toBe(true);
    });

    it('a server error clears when the user edits the field', async () => {
      const { root, fixture } = await failSave({ kind: 'validation', message: 'x', fields: { fullName: ['Invalid value'] } });
      type(root, 'user-full-name-input', 'Other');
      expect(fixture.componentInstance['form'].controls.fullName.hasError('server')).toBe(false);
    });
  });

  describe('deactivate', () => {
    it('asks for confirmation and only then deactivates', async () => {
      const { fixture, facade, confirm } = setup({ id: 'u-1' }, user);
      const root = await render(fixture);

      confirm.confirm.mockResolvedValueOnce(false);
      q(root, 'user-deactivate-button')?.click();
      await fixture.whenStable();
      expect(facade.deactivate).not.toHaveBeenCalled();

      q(root, 'user-deactivate-button')?.click();
      await fixture.whenStable();
      expect(confirm.confirm).toHaveBeenCalledWith(expect.objectContaining({ destructive: true }));
      expect(facade.deactivate).toHaveBeenCalledWith('u-1');
    });

    it('an inactive user offers no deactivate action', async () => {
      const { fixture } = setup({ id: 'u-1' }, { ...user, isActive: false });
      expect(q(await render(fixture), 'user-deactivate-button')).toBeNull();
    });
  });

  it('the assignments tab only exists for a saved user', async () => {
    const created = setup({});
    const newRoot = await render(created.fixture);
    expect(Array.from(newRoot.querySelectorAll('[role="tab"]')).map((t) => t.textContent)).not.toContain('org.users.detail.tabs.assignments');
    TestBed.resetTestingModule();

    const { fixture } = setup({ id: 'u-1' }, user);
    const root = await render(fixture);
    expect(Array.from(root.querySelectorAll('[role="tab"]')).map((t) => t.textContent?.trim())).toContain('org.users.detail.tabs.assignments');
  });

  it('the assignments tab needs ROLE_ASSIGNMENT:READ', async () => {
    const { fixture } = setup({ id: 'u-1' }, user);
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'USER:READ', scopes: [] } as never]);
    const labels = Array.from((await render(fixture)).querySelectorAll('[role="tab"]')).map((t) => t.textContent?.trim());
    expect(labels).not.toContain('org.users.detail.tabs.assignments');
    expect(labels).toContain('common.tabs.general');
  });
});
