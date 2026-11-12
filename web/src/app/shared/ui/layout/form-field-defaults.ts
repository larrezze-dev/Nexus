import { Provider } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS, MatFormFieldDefaultOptions } from '@angular/material/form-field';

/** App-wide `mat-form-field` defaults (`conventions-frontend.md §4.1`): outline, label inside the field. */
export const NEXUS_FORM_FIELD_DEFAULTS: MatFormFieldDefaultOptions = {
  appearance: 'outline',
};

/** Add once to `app.config.ts`: every `mat-form-field` uses `appearance="outline"` unless it says otherwise. */
export function provideNexusFormFieldDefaults(): Provider {
  return { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: NEXUS_FORM_FIELD_DEFAULTS };
}
