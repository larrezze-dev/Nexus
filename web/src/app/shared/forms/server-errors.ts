import { AbstractControl } from '@angular/forms';

/**
 * Marks the controls named in `errors` (a 400/409 field → messages map) as invalid and touched,
 * so their `mat-error` is shown. The error clears itself when the user edits the value, because
 * Angular re-runs the control's validators on every change.
 */
export function markServerErrors(
  controls: Readonly<Record<string, AbstractControl | undefined>>,
  errors: Readonly<Record<string, readonly string[]>>,
): void {
  for (const field of Object.keys(errors)) {
    const control = controls[field];
    if (control && errors[field].length > 0) {
      control.setErrors({ ...(control.errors ?? {}), server: true });
      control.markAsTouched();
    }
  }
}
