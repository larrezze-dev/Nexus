import { Warehouse } from '../../../api/platform/models/warehouse';
import {
  createWarehouseForm,
  patchWarehouseForm,
  toCreateLocationRequest,
  warehouseFormValue,
} from './warehouse-form';

function fillBase(form: ReturnType<typeof createWarehouseForm>): void {
  form.patchValue({
    code: 'WH-1',
    captionI18n: { de: 'Lager', en: '', fr: '', it: '', es: '' },
    legalEntityId: 'le-1',
  });
}

const fullNewLocation = { code: '', name: 'Depot', street: '', postalCode: '', city: 'Zürich', region: '', countryCode: 'CH' };

describe('warehouse form', () => {
  it('starts in "existing location" mode: locationId enabled and required, new-location group disabled', () => {
    const form = createWarehouseForm();
    fillBase(form);
    expect(form.controls.addressMode.value).toBe('existingLocation');
    expect(form.controls.locationId.enabled).toBe(true);
    expect(form.controls.newLocation.disabled).toBe(true);
    expect(form.valid).toBe(false);

    form.controls.locationId.setValue('loc-1');
    expect(form.valid).toBe(true);
  });

  it('"new address" mode requires name and a two-letter upper-case country code, and ignores locationId', () => {
    const form = createWarehouseForm();
    fillBase(form);
    form.controls.addressMode.setValue('newLocation');
    expect(form.controls.locationId.disabled).toBe(true);
    expect(form.controls.newLocation.enabled).toBe(true);
    expect(form.valid).toBe(false);

    form.controls.newLocation.patchValue({ name: 'Depot', countryCode: 'ch' });
    expect(form.controls.newLocation.controls.countryCode.errors).toHaveProperty('pattern');
    expect(form.valid).toBe(false);

    form.controls.newLocation.patchValue(fullNewLocation);
    expect(form.valid).toBe(true);
  });

  it('"same as legal entity" is unavailable: the form is invalid with it', () => {
    const form = createWarehouseForm();
    fillBase(form);
    form.controls.addressMode.setValue('sameAsLegalEntity');
    expect(form.controls.addressMode.errors).toHaveProperty('addressModeUnavailable');
    expect(form.valid).toBe(false);
    expect(form.controls.locationId.disabled).toBe(true);
    expect(form.controls.newLocation.disabled).toBe(true);
  });

  it('requires code, legal entity and the German caption', () => {
    const form = createWarehouseForm();
    form.controls.locationId.setValue('loc-1');
    expect(form.controls.code.errors).toHaveProperty('required');
    expect(form.controls.legalEntityId.errors).toHaveProperty('required');
    expect(form.controls.captionI18n.errors).toHaveProperty('germanCaptionRequired');

    fillBase(form);
    expect(form.valid).toBe(true);
    form.controls.captionI18n.setValue({ de: '   ', en: 'Store' });
    expect(form.controls.captionI18n.errors).toHaveProperty('germanCaptionRequired');
  });

  it('patchWarehouseForm fills the edit form, locks the legal entity and keeps it valid', () => {
    const form = createWarehouseForm();
    patchWarehouseForm(form, {
      id: 'w1',
      code: 'WH-9',
      captionI18n: { de: 'Lager' },
      legalEntityId: 'le-9',
      businessUnitId: 'bu-9',
      locationId: 'loc-9',
      isActive: true,
    } as unknown as Warehouse);

    expect(form.controls.legalEntityId.disabled).toBe(true);
    expect(form.valid).toBe(true);
    expect(warehouseFormValue(form)).toMatchObject({
      code: 'WH-9',
      legalEntityId: 'le-9',
      businessUnitId: 'bu-9',
      addressMode: 'existingLocation',
      locationId: 'loc-9',
    });
  });

  it('toCreateLocationRequest sends blank optional fields as null and trims', () => {
    expect(toCreateLocationRequest({ ...fullNewLocation, name: ' Depot ' })).toEqual({
      code: null,
      name: 'Depot',
      street: null,
      postalCode: null,
      city: 'Zürich',
      region: null,
      countryCode: 'CH',
    });
  });
});
