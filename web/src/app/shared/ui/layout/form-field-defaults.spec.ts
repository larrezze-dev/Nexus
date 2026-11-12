import { TestBed } from '@angular/core/testing';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { provideNexusFormFieldDefaults } from './form-field-defaults';

describe('provideNexusFormFieldDefaults', () => {
  it('makes outline the default mat-form-field appearance', () => {
    TestBed.configureTestingModule({ providers: [provideNexusFormFieldDefaults()] });

    expect(TestBed.inject(MAT_FORM_FIELD_DEFAULT_OPTIONS).appearance).toBe('outline');
  });
});
