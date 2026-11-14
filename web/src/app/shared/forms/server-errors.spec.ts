import { FormControl, FormGroup } from '@angular/forms';
import { markServerErrors } from './server-errors';

describe('markServerErrors', () => {
  it('marks the named controls invalid and touched, and leaves the others alone', () => {
    const form = new FormGroup({ code: new FormControl('A'), name: new FormControl('B') });

    markServerErrors(form.controls, { code: ['Already exists'] });

    expect(form.controls.code.hasError('server')).toBe(true);
    expect(form.controls.code.touched).toBe(true);
    expect(form.controls.name.valid).toBe(true);
  });

  it('keeps the control validators’ own errors and ignores unknown fields', () => {
    const control = new FormControl('', { validators: () => ({ required: true }) });
    const form = new FormGroup({ code: control });

    markServerErrors(form.controls, { code: ['x'], missing: ['y'] });

    expect(control.errors).toEqual({ required: true, server: true });
  });

  it('clears itself when the user edits the value', () => {
    const control = new FormControl('A');
    markServerErrors({ code: control }, { code: ['x'] });

    control.setValue('B');

    expect(control.valid).toBe(true);
  });
});
