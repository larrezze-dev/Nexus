import { AbstractControl, FormControl, FormGroup, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { emptyI18nText, I18nText } from '../../../shared/ui/i18n-text.model';
import { AddressMode, NewLocationValue, SAME_AS_LEGAL_ENTITY_AVAILABLE, WarehouseFormValue } from './warehouse.models';

/** `CreateLocationRequest.countryCode` pattern in the contract: ISO 3166-1 alpha-2, upper case. */
export const COUNTRY_CODE_PATTERN = /^[A-Z]{2}$/;

/** `captionI18n.de` is the mandatory reference language (`I18nText` in the contract). */
export const germanCaptionRequired: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const caption = control.value as I18NText | null;
  return caption?.de?.trim() ? null : { germanCaptionRequired: true };
};

/** The «same as legal entity» option has no API yet: the form cannot be saved with it. */
export const addressModeAvailable: ValidatorFn = (control: AbstractControl): ValidationErrors | null =>
  control.value === 'sameAsLegalEntity' && !SAME_AS_LEGAL_ENTITY_AVAILABLE ? { addressModeUnavailable: true } : null;

export function createNewLocationGroup() {
  return new FormGroup({
    code: new FormControl('', { nonNullable: true }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    street: new FormControl('', { nonNullable: true }),
    postalCode: new FormControl('', { nonNullable: true }),
    city: new FormControl('', { nonNullable: true }),
    region: new FormControl('', { nonNullable: true }),
    countryCode: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(COUNTRY_CODE_PATTERN)],
    }),
  });
}

export type WarehouseForm = ReturnType<typeof createWarehouseForm>;

/**
 * Typed form for the warehouse create/edit screen. Only the controls of the chosen address
 * mode are enabled (`locationId` for `existingLocation`, the `newLocation` group for
 * `newLocation`), so a disabled control never blocks validity. Use `getRawValue()` — the
 * result matches `WarehouseFormValue` (see `warehouseFormValue()`).
 */
export function createWarehouseForm() {
  const form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    captionI18n: new FormControl<I18nText>(emptyI18nText(), {
      nonNullable: true,
      validators: [germanCaptionRequired],
    }),
    legalEntityId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    businessUnitId: new FormControl<string | null>(null),
    addressMode: new FormControl<AddressMode>('existingLocation', {
      nonNullable: true,
      validators: [addressModeAvailable],
    }),
    locationId: new FormControl<string | null>(null, { validators: [Validators.required] }),
    newLocation: createNewLocationGroup(),
  });
  applyAddressMode(form, form.controls.addressMode.value);
  form.controls.addressMode.valueChanges.subscribe((mode) => applyAddressMode(form, mode));
  return form;
}

/** Enables only the address controls the chosen mode needs. */
export function applyAddressMode(form: WarehouseForm, mode: AddressMode): void {
  const useExisting = mode === 'existingLocation';
  const useNew = mode === 'newLocation';
  setEnabled(form.controls.locationId, useExisting);
  setEnabled(form.controls.newLocation, useNew);
}

function setEnabled(control: AbstractControl, enabled: boolean): void {
  if (enabled && control.disabled) {
    control.enable({ emitEvent: false });
  } else if (!enabled && control.enabled) {
    control.disable({ emitEvent: false });
  }
}

/** Fills the form from a loaded warehouse (always `existingLocation`; `legalEntityId` is immutable). */
export function patchWarehouseForm(form: WarehouseForm, warehouse: Warehouse): void {
  form.patchValue({
    code: warehouse.code,
    captionI18n: warehouse.captionI18n as I18nText,
    legalEntityId: warehouse.legalEntityId,
    businessUnitId: warehouse.businessUnitId ?? null,
    addressMode: 'existingLocation',
    locationId: warehouse.locationId,
  });
  applyAddressMode(form, 'existingLocation');
  form.controls.legalEntityId.disable({ emitEvent: false });
  form.markAsPristine();
}

export function warehouseFormValue(form: WarehouseForm): WarehouseFormValue {
  return form.getRawValue() as WarehouseFormValue;
}

/** Blank optional strings become `null` in the `CreateLocationRequest`. */
export function toCreateLocationRequest(value: NewLocationValue) {
  const orNull = (text: string) => (text.trim() ? text.trim() : null);
  return {
    code: orNull(value.code),
    name: value.name.trim(),
    street: orNull(value.street),
    postalCode: orNull(value.postalCode),
    city: orNull(value.city),
    region: orNull(value.region),
    countryCode: value.countryCode.trim(),
  };
}
