import { I18NText } from '../../../api/platform/models/i-18-n-text';

/**
 * How the warehouse address is provided (decided with Luciano, PR-5):
 * - `sameAsLegalEntity`: reuse the legal entity's address. NOT available yet: it needs a
 *   `party_location` API that does not exist, so the option is shown disabled and the form
 *   refuses to save with it (`addressModeUnavailable` error).
 * - `existingLocation`: pick one of the existing `Location`s (`warehouse.locationId`).
 * - `newLocation`: create a `Location` first (`POST /locations`), then the warehouse with its id.
 */
export type AddressMode = 'sameAsLegalEntity' | 'existingLocation' | 'newLocation';

/** Whether the «same as legal entity» address option can be used (needs a not-yet-built API). */
export const SAME_AS_LEGAL_ENTITY_AVAILABLE = false;

/** Raw value of the «new address» sub-form: blank optional fields are sent as `null`. */
export interface NewLocationValue {
  readonly code: string;
  readonly name: string;
  readonly street: string;
  readonly postalCode: string;
  readonly city: string;
  readonly region: string;
  readonly countryCode: string;
}

/** `WarehouseForm.getRawValue()`: includes the disabled (immutable/inactive) controls. */
export interface WarehouseFormValue {
  readonly code: string;
  readonly captionI18n: I18NText;
  readonly legalEntityId: string;
  readonly businessUnitId: string | null;
  readonly addressMode: AddressMode;
  readonly locationId: string | null;
  readonly newLocation: NewLocationValue;
}

/** A legal entity as the warehouse screens show it (code + legal name, never the id). */
export interface WarehouseLegalEntityRef {
  readonly id: string;
  readonly code: string;
  readonly legalName: string | null;
}

/** A business unit: label is resolved from `captionI18n` in the active language. */
export interface WarehouseBusinessUnitRef {
  readonly id: string;
  readonly code: string;
  readonly captionI18n: I18NText | null;
}

/** A location: code (optional) + name. */
export interface WarehouseLocationRef {
  readonly id: string;
  readonly code: string | null;
  readonly name: string;
}

/**
 * The second step of a non-atomic save failed (or the first one did). `locationCreated` tells
 * the UI the new `Location` already exists (`locationId`): retry reuses it, never duplicates it.
 */
export class WarehouseSaveError extends Error {
  constructor(
    readonly step: 'location' | 'warehouse',
    override readonly cause: unknown,
    readonly locationId: string | null,
  ) {
    super(`warehouse save failed at step "${step}"`);
    this.name = 'WarehouseSaveError';
  }

  get locationCreated(): boolean {
    return this.step === 'warehouse' && this.locationId !== null;
  }
}
