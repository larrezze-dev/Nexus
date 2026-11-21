import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import type { CreateRoleAssignmentRequest } from '../../../api/platform/models/create-role-assignment-request';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';

/**
 * `scopeType`s the grant form offers (`ACC-R1`). `GROUP` has no picker backing it yet and
 * `PROJECT` does not exist before step 2.1 (ACC-R2 rejects it).
 */
export type GrantableScopeType = 'LEGAL_ENTITY' | 'BUSINESS_UNIT' | 'WAREHOUSE' | 'GLOBAL';
export const GRANTABLE_SCOPE_TYPES: readonly GrantableScopeType[] = [
  'LEGAL_ENTITY',
  'BUSINESS_UNIT',
  'WAREHOUSE',
  'GLOBAL',
];

/** A picked scope entity: its id and the name shown for it. */
export interface ScopeEntityOption {
  readonly id: string;
  readonly label: string;
}

export type RoleAssignmentForm = {
  role: FormControl<RoleListItem | null>;
  scopeType: FormControl<GrantableScopeType>;
  /** Disabled (and so skipped by validation) for `GLOBAL`, which has no entity (ACC-R1). */
  scopeEntity: FormControl<ScopeEntityOption | null>;
  /** `Date`s for `mat-datepicker`; converted to UTC instants by `toCreateRoleAssignmentRequest`. */
  validFrom: FormControl<Date | null>;
  validTo: FormControl<Date | null>;
};

/**
 * «Until» before «from» is invalid. Same day is fine: «until» is the last day (inclusive).
 * Compares calendar days, ignoring the time of day.
 */
export function dateRangeValidator(control: AbstractControl): ValidationErrors | null {
  const to = control.value as Date | null;
  const from = control.parent?.get('validFrom')?.value as Date | null | undefined;
  if (!to || !from) {
    return null;
  }
  return dayNumber(to) < dayNumber(from) ? { dateRange: true } : null;
}

/** Typed form of «New assignment». Changing the scope type clears and enables/disables the entity. */
export function createRoleAssignmentForm(): FormGroup<RoleAssignmentForm> {
  const form = new FormGroup<RoleAssignmentForm>({
    role: new FormControl<RoleListItem | null>(null, { validators: [Validators.required] }),
    scopeType: new FormControl<GrantableScopeType>('LEGAL_ENTITY', { nonNullable: true }),
    scopeEntity: new FormControl<ScopeEntityOption | null>(null, { validators: [Validators.required] }),
    validFrom: new FormControl<Date | null>(null, { validators: [Validators.required] }),
    validTo: new FormControl<Date | null>(null, { validators: [dateRangeValidator] }),
  });
  form.controls.scopeType.valueChanges.subscribe((type) => {
    form.controls.scopeEntity.setValue(null);
    if (type === 'GLOBAL') {
      form.controls.scopeEntity.disable();
    } else {
      form.controls.scopeEntity.enable();
    }
  });
  form.controls.validFrom.valueChanges.subscribe(() => form.controls.validTo.updateValueAndValidity());
  return form;
}

/** Empties the form after a grant (back to a legal-entity scope with the entity enabled). */
export function resetRoleAssignmentForm(form: FormGroup<RoleAssignmentForm>): void {
  form.reset({ role: null, scopeType: 'LEGAL_ENTITY', scopeEntity: null, validFrom: null, validTo: null });
  form.controls.scopeEntity.enable();
}

/**
 * `POST /v1/role-assignments` body. The datepicker's calendar day becomes that day's UTC
 * midnight. `valid_to` is exclusive in the database while the form's «until» day is inclusive, so
 * it is sent as the NEXT midnight (same-day from/until stays valid: `valid_to > valid_from`).
 * `GLOBAL` has no entity (`scopeEntityId: null`, ACC-R1).
 *
 * @throws Error if the form is incomplete (callers check `form.valid` first).
 */
export function toCreateRoleAssignmentRequest(
  userId: string,
  value: ReturnType<FormGroup<RoleAssignmentForm>['getRawValue']>,
): CreateRoleAssignmentRequest {
  if (!value.role || !value.validFrom) {
    throw new Error('role and validFrom are required');
  }
  const global = value.scopeType === 'GLOBAL';
  if (!global && !value.scopeEntity) {
    throw new Error('scopeEntity is required unless the scope is GLOBAL');
  }
  return {
    roleId: value.role.id,
    userId,
    scopeType: value.scopeType,
    scopeEntityId: global ? null : (value.scopeEntity?.id ?? null),
    validFrom: utcMidnight(value.validFrom, 0),
    validTo: value.validTo ? utcMidnight(value.validTo, 1) : null,
  };
}

function utcMidnight(day: Date, plusDays: number): string {
  return new Date(Date.UTC(day.getFullYear(), day.getMonth(), day.getDate() + plusDays)).toISOString();
}

function dayNumber(day: Date): number {
  return Date.UTC(day.getFullYear(), day.getMonth(), day.getDate());
}
