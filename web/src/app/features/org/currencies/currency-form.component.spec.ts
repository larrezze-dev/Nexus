import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { Currency } from '../../../api/platform/models/currency';
import { CurrencyFormComponent, CurrencyFormValue } from './currency-form.component';

const translations = {
  en: {
    org: {
      currencies: {
        fields: { code: 'Code', minorUnits: 'Decimal places' },
        sections: { currency: 'Currency', names: 'Names' },
        hints: { code: 'Three letters.', minorUnits: 'Decimals.', lockedAfterCreate: 'Set at creation.' },
        errors: { codeFormat: 'Three capitals.', minorUnitsRange: '0 to 4.' },
        new: 'New currency',
        actions: { create: 'Create currency' },
      },
    },
    common: { cancel: 'Cancel', saveChanges: 'Save changes' },
    shared: { form: { required: 'Required', reviewFields: '{{count}} fields', reviewFieldsOne: '1 field' } },
  },
};

@Component({
  standalone: true,
  imports: [CurrencyFormComponent],
  template: `<nx-currency-form
    [mode]="mode()"
    [initial]="initial()"
    [fieldErrors]="fieldErrors()"
    [readOnly]="readOnly()"
    (save)="saved = $event"
  />`,
})
class HostComponent {
  readonly mode = signal<'create' | 'edit'>('create');
  readonly initial = signal<Currency | null>(null);
  readonly fieldErrors = signal<Record<string, string[]>>({});
  readonly readOnly = signal(false);
  saved: CurrencyFormValue | null = null;
}

const sek: Currency = { code: 'SEK', captionI18n: { de: 'Schwedische Krone' }, minorUnits: 2, isActive: true };

async function setup() {
  await TestBed.configureTestingModule({
    imports: [
      HostComponent,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [provideNoopAnimations()],
  }).compileComponents();
  const fixture: ComponentFixture<HostComponent> = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

function el(fixture: ComponentFixture<unknown>, testId: string): HTMLInputElement {
  return (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
}

function type(fixture: ComponentFixture<unknown>, input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function german(fixture: ComponentFixture<unknown>): HTMLInputElement {
  return (fixture.nativeElement as HTMLElement).querySelector(
    '[formcontrolname="captionI18n"] input[data-lang="de"]',
  ) as HTMLInputElement;
}

function submit(fixture: ComponentFixture<unknown>): void {
  el(fixture, 'currency-form-submit').click();
  fixture.detectChanges();
}

describe('CurrencyFormComponent', () => {
  it('create: starts with 2 decimals and everything editable', async () => {
    const fixture = await setup();

    expect(el(fixture, 'currency-form-minor-units').value).toBe('2');
    expect(el(fixture, 'currency-form-code').disabled).toBe(false);
  });

  it('create: types the code in capitals (ISO 4217)', async () => {
    const fixture = await setup();
    const code = el(fixture, 'currency-form-code');

    type(fixture, code, 'sek');

    expect(code.value).toBe('SEK');
  });

  it('create: refuses a code that is not three letters and says so', async () => {
    const fixture = await setup();
    type(fixture, el(fixture, 'currency-form-code'), 'SE');
    type(fixture, german(fixture), 'Krone');

    submit(fixture);

    expect(fixture.componentInstance.saved).toBeNull();
    expect(el(fixture, 'currency-form-code-format')?.textContent).toContain('Three capitals.');
  });

  it('create: refuses a missing German name and decimals outside 0..4', async () => {
    const fixture = await setup();
    type(fixture, el(fixture, 'currency-form-code'), 'SEK');
    type(fixture, el(fixture, 'currency-form-minor-units'), '5');

    submit(fixture);

    expect(fixture.componentInstance.saved).toBeNull();
    expect(el(fixture, 'currency-form-minor-units-range')).toBeTruthy();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="currency-form-invalid"]')).toBeTruthy();
  });

  it('create: refuses fractional decimals such as 2.5', async () => {
    const fixture = await setup();
    type(fixture, el(fixture, 'currency-form-code'), 'SEK');
    type(fixture, german(fixture), 'Krone');
    type(fixture, el(fixture, 'currency-form-minor-units'), '2.5');

    submit(fixture);

    expect(fixture.componentInstance.saved).toBeNull();
    expect(el(fixture, 'currency-form-minor-units-range')).toBeTruthy();
  });

  it('create: emits code, names and decimals as a number when valid', async () => {
    const fixture = await setup();
    type(fixture, el(fixture, 'currency-form-code'), 'sek');
    type(fixture, german(fixture), 'Schwedische Krone');
    type(fixture, el(fixture, 'currency-form-minor-units'), '0');

    submit(fixture);

    expect(fixture.componentInstance.saved).toEqual({
      code: 'SEK',
      captionI18n: expect.objectContaining({ de: 'Schwedische Krone' }),
      minorUnits: 0,
    });
  });

  it('shows a server error on the code and marks the control (409 CONFLICT)', async () => {
    const fixture = await setup();

    fixture.componentInstance.fieldErrors.set({ code: ['A record with this code already exists.'] });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el(fixture, 'currency-form-code-server')?.textContent).toContain('already exists');
    expect(el(fixture, 'currency-form-code').classList.contains('ng-invalid')).toBe(true);
    // The focus moves to the field the server refused.
    expect(document.activeElement).toBe(el(fixture, 'currency-form-code'));
  });

  it('create: the primary button says «Create currency», Cancel is a text button', async () => {
    const fixture = await setup();

    expect(el(fixture, 'currency-form-submit').textContent?.trim()).toBe('Create currency');
    const cancel = el(fixture, 'currency-form-cancel');
    expect(cancel.hasAttribute('mat-button')).toBe(true);
    expect(cancel.hasAttribute('mat-stroked-button')).toBe(false);
  });

  it('create: an invalid submit focuses the first invalid field (the code)', async () => {
    const fixture = await setup();

    submit(fixture);

    expect(document.activeElement).toBe(el(fixture, 'currency-form-code'));
  });

  it('read-only: the form carries .nx-readonly (disabled fields look locked) and has no Cancel; editable forms do', async () => {
    const fixture = await setup();
    const form = (fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement;
    expect(form.classList).not.toContain('nx-readonly');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="currency-form-cancel"]')).not.toBeNull();

    fixture.componentInstance.mode.set('edit');
    fixture.componentInstance.initial.set(sek);
    fixture.componentInstance.readOnly.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(form.classList).toContain('nx-readonly');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="currency-form-cancel"]')).toBeNull();
    expect(german(fixture).disabled).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('.nx-i18n-caption-editor')?.classList).toContain(
      'nx-readonly',
    );
  });

  it('edit: code and decimals are locked, the name is editable, Save needs a change', async () => {
    const fixture = await setup();
    fixture.componentInstance.mode.set('edit');
    fixture.componentInstance.initial.set(sek);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el(fixture, 'currency-form-code').disabled).toBe(true);
    expect(el(fixture, 'currency-form-minor-units').disabled).toBe(true);
    // Locked look: lock icon and a hint saying why, on both fields.
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('.nx-field--locked').length).toBe(2);
    // Projected into Material's slots (suffix and hint line), not left inside the input row.
    expect(root.querySelectorAll('.nx-field--locked .mat-mdc-form-field-icon-suffix mat-icon').length).toBe(2);
    expect(
      [...root.querySelectorAll('.nx-field--locked .mat-mdc-form-field-hint-wrapper mat-hint')].map((hint) =>
        hint.textContent?.trim(),
      ),
    ).toEqual(['Set at creation.', 'Set at creation.']);
    expect(german(fixture).value).toBe('Schwedische Krone');
    expect(german(fixture).disabled).toBe(false);
    expect(el(fixture, 'currency-form-submit').disabled).toBe(true);

    type(fixture, german(fixture), 'Krone');
    expect(el(fixture, 'currency-form-submit').disabled).toBe(false);
    submit(fixture);

    expect(fixture.componentInstance.saved).toEqual({
      code: 'SEK',
      captionI18n: expect.objectContaining({ de: 'Krone' }),
      minorUnits: 2,
    });
  });
});
