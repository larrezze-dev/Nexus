import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import {
  createRoleAssignmentForm,
  resetRoleAssignmentForm,
  toCreateRoleAssignmentRequest,
} from './role-assignment-form';

const role = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0d02',
  code: 'ORG_ADMIN',
  captionI18n: { de: 'Org-Admin' },
  isActive: true,
  isSystem: false,
} as RoleListItem;
const userId = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0001';

describe('role assignment form', () => {
  it('requires role, entity (unless GLOBAL) and a start date', () => {
    const form = createRoleAssignmentForm();
    expect(form.valid).toBe(false);
    expect(form.controls.role.hasError('required')).toBe(true);
    expect(form.controls.scopeEntity.hasError('required')).toBe(true);
    expect(form.controls.validFrom.hasError('required')).toBe(true);
  });

  it('ACC-R1: GLOBAL has no entity: the control is disabled, cleared and not required', () => {
    const form = createRoleAssignmentForm();
    form.controls.scopeEntity.setValue({ id: 'x', label: 'LE' });
    form.controls.scopeType.setValue('GLOBAL');

    expect(form.controls.scopeEntity.disabled).toBe(true);
    expect(form.controls.scopeEntity.value).toBeNull();

    form.controls.role.setValue(role);
    form.controls.validFrom.setValue(new Date(2026, 10, 14));
    expect(form.valid).toBe(true);

    form.controls.scopeType.setValue('WAREHOUSE');
    expect(form.controls.scopeEntity.enabled).toBe(true);
    expect(form.valid).toBe(false);
  });

  it('until before from is invalid; the same day is fine', () => {
    const form = createRoleAssignmentForm();
    form.controls.validFrom.setValue(new Date(2026, 10, 14));
    form.controls.validTo.setValue(new Date(2026, 10, 13));
    expect(form.controls.validTo.hasError('dateRange')).toBe(true);

    form.controls.validTo.setValue(new Date(2026, 10, 14, 23, 59));
    expect(form.controls.validTo.hasError('dateRange')).toBe(false);
  });

  it('changing "from" re-validates "until"', () => {
    const form = createRoleAssignmentForm();
    form.controls.validFrom.setValue(new Date(2026, 10, 10));
    form.controls.validTo.setValue(new Date(2026, 10, 12));
    expect(form.controls.validTo.valid).toBe(true);

    form.controls.validFrom.setValue(new Date(2026, 10, 20));
    expect(form.controls.validTo.hasError('dateRange')).toBe(true);
  });

  it('builds the request: calendar days as Zurich day starts, the inclusive last day sent as the next Zurich day start', () => {
    const form = createRoleAssignmentForm();
    form.controls.role.setValue(role);
    form.controls.scopeEntity.setValue({ id: 'le-1', label: 'LE — Nüssli' });
    form.controls.validFrom.setValue(new Date(2026, 10, 14, 15, 30));
    form.controls.validTo.setValue(new Date(2026, 10, 30));

    expect(toCreateRoleAssignmentRequest(userId, form.getRawValue())).toEqual({
      roleId: role.id,
      userId,
      scopeType: 'LEGAL_ENTITY',
      scopeEntityId: 'le-1',
      validFrom: '2026-11-13T23:00:00.000Z',
      validTo: '2026-11-30T23:00:00.000Z',
    });
  });

  it('uses the Zurich offset in force on each day across DST changes (late March and late October)', () => {
    const form = createRoleAssignmentForm();
    form.controls.role.setValue(role);
    form.controls.scopeType.setValue('GLOBAL');
    // 28 Mar: CET (+1) -> 29 Mar 00:00 Zurich = 28 Mar 23:00Z; the exclusive end (start of 30 Mar) is CEST (+2)
    form.controls.validFrom.setValue(new Date(2026, 2, 28));
    form.controls.validTo.setValue(new Date(2026, 2, 29));
    let request = toCreateRoleAssignmentRequest(userId, form.getRawValue());
    expect(request.validFrom).toBe('2026-03-27T23:00:00.000Z');
    expect(request.validTo).toBe('2026-03-29T22:00:00.000Z');
    // 25 Oct: CEST until 03:00; the start of 26 Oct is CET (+1)
    form.controls.validFrom.setValue(new Date(2026, 9, 25));
    form.controls.validTo.setValue(new Date(2026, 9, 25));
    request = toCreateRoleAssignmentRequest(userId, form.getRawValue());
    expect(request.validFrom).toBe('2026-10-24T22:00:00.000Z');
    expect(request.validTo).toBe('2026-10-25T23:00:00.000Z');
  });

  it('GLOBAL sends scopeEntityId null and no end date as null', () => {
    const form = createRoleAssignmentForm();
    form.controls.role.setValue(role);
    form.controls.scopeType.setValue('GLOBAL');
    form.controls.validFrom.setValue(new Date(2026, 0, 1));

    const request = toCreateRoleAssignmentRequest(userId, form.getRawValue());
    expect(request.scopeEntityId).toBeNull();
    expect(request.validTo).toBeNull();
  });

  it('refuses an incomplete form instead of sending garbage', () => {
    const form = createRoleAssignmentForm();
    expect(() => toCreateRoleAssignmentRequest(userId, form.getRawValue())).toThrow();
  });

  it('resetRoleAssignmentForm() goes back to an empty legal-entity form with the entity enabled', () => {
    const form = createRoleAssignmentForm();
    form.controls.scopeType.setValue('GLOBAL');
    form.controls.role.setValue(role);
    resetRoleAssignmentForm(form);

    expect(form.controls.scopeType.value).toBe('LEGAL_ENTITY');
    expect(form.controls.role.value).toBeNull();
    expect(form.controls.scopeEntity.enabled).toBe(true);
  });
});
