import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import type { Party } from '../../../api/platform/models/party';
import { PartyFormComponent, type PartyFormValue } from './party-form.component';

const party: Party = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c20',
  partyType: 'PERSON',
  legalName: 'Jane Doe',
  displayName: 'Jane',
  taxId: null,
  erpCompanyCode: 'JD01',
  isInternal: true,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const en = {
  org: {
    parties: {
      fields: {
        partyType: 'Type',
        legalName: 'Legal name',
        legalNamePerson: 'Full name',
        displayName: 'Short name',
        taxId: 'Tax ID',
        erpCompanyCode: 'ERP company code',
        isInternal: 'Scope',
      },
      type: { ORGANIZATION: 'Organization', PERSON: 'Person' },
      scope: { internal: 'Internal', external: 'External' },
      sections: { identification: 'Identification', taxErp: 'Tax and ERP' },
      hints: { lockedAfterCreate: 'Set when the party is created.' },
      actions: { create: 'Create party' },
    },
  },
  shared: { form: { required: 'Required', reviewFields: 'Check the {{count}} fields.', reviewFieldsOne: 'Check the field.' } },
  common: { cancel: 'Cancel', saveChanges: 'Save changes' },
};

type FormInternals = {
  form: { controls: Record<string, { value: unknown; disabled: boolean; setValue(value: unknown): void }> };
  onSubmit(): void;
};

async function setup(mode: 'create' | 'edit', initial: Party | null = null, readOnly = false) {
  await TestBed.configureTestingModule({
    imports: [
      PartyFormComponent,
      TranslocoTestingModule.forRoot({
        langs: { en },
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [provideNoopAnimations()],
  }).compileComponents();

  const fixture: ComponentFixture<PartyFormComponent> = TestBed.createComponent(PartyFormComponent);
  fixture.componentRef.setInput('mode', mode);
  fixture.componentRef.setInput('initial', initial);
  fixture.componentRef.setInput('readOnly', readOnly);
  fixture.detectChanges();
  // mat-select initialises its selection in a microtask: then render the triggers.
  await fixture.whenStable();
  fixture.detectChanges();
  const emitted: PartyFormValue[] = [];
  fixture.componentInstance.save.subscribe((value) => emitted.push(value));
  return { fixture, emitted, form: fixture.componentInstance as unknown as FormInternals };
}

function root(fixture: ComponentFixture<PartyFormComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

describe('PartyFormComponent', () => {
  it('groups the fields in the Identification and Tax and ERP sections on the form grid', async () => {
    const { fixture } = await setup('create');

    const titles = [...root(fixture).querySelectorAll('nx-form-section h2')].map((h) => h.textContent?.trim());
    expect(titles).toEqual(['Identification', 'Tax and ERP']);
    expect(root(fixture).querySelectorAll('mat-form-field:not([nxSpan])').length).toBe(0);
  });

  it('creates external parties only: no scope selector on create, isInternal=false is sent', async () => {
    const { fixture, form, emitted } = await setup('create');

    expect(root(fixture).querySelector('[data-testid="party-form-scope"]')).toBeNull();
    form.form.controls['legalName'].setValue('Acme AG');
    root(fixture).querySelector<HTMLButtonElement>('[data-testid="party-form-submit"]')?.click();
    expect(emitted).toHaveLength(1);
    expect(emitted[0].isInternal).toBe(false);
  });

  it('defaults a new party to an external organization with an editable type', async () => {
    const { form } = await setup('create');

    expect(form.form.controls['partyType'].value).toBe('ORGANIZATION');
    expect(form.form.controls['isInternal'].value).toBe(false);
    expect(form.form.controls['partyType'].disabled).toBe(false);
  });

  it('labels the name «Full name» for a person and «Legal name» for an organization', async () => {
    const { fixture, form } = await setup('create');
    const nameLabel = () =>
      root(fixture).querySelector('[data-testid="party-form-legal-name"]')?.closest('mat-form-field')?.querySelector('mat-label')?.textContent?.trim();

    expect(nameLabel()).toBe('Legal name');
    form.form.controls['partyType'].setValue('PERSON');
    fixture.detectChanges();
    expect(nameLabel()).toBe('Full name');
  });

  it('locks type and scope in edit mode with the reason, showing translated values (ORG-R2)', async () => {
    const { fixture, form } = await setup('edit', party);

    expect(form.form.controls['partyType'].disabled).toBe(true);
    expect(form.form.controls['isInternal'].disabled).toBe(true);
    expect(form.form.controls['legalName'].disabled).toBe(false);
    const locked = root(fixture).querySelectorAll('mat-form-field.nx-field--locked');
    expect(locked.length).toBe(2);
    expect(locked[0].textContent).toContain('Person');
    expect(locked[0].textContent).toContain('Set when the party is created.');
    expect(locked[1].textContent).toContain('Internal');
    expect(root(fixture).textContent).not.toContain('PERSON');
    expect(root(fixture).textContent).not.toContain('true');
  });

  it('on an invalid submit shows the alert with the number of fields and does not emit', async () => {
    const { fixture, emitted, form } = await setup('create');

    form.onSubmit();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(root(fixture).querySelector('[data-testid="party-form-invalid"]')?.textContent).toContain('Check the field.');
    expect(document.activeElement?.getAttribute('formcontrolname')).toBe('legalName');
  });

  it('emits trimmed values on a valid submit', async () => {
    const { emitted, form } = await setup('create');

    form.form.controls['legalName'].setValue('  Acme Events SA ');
    form.form.controls['erpCompanyCode'].setValue(' ACME01 ');
    form.onSubmit();

    expect(emitted).toEqual([
      {
        partyType: 'ORGANIZATION',
        isInternal: false,
        legalName: 'Acme Events SA',
        displayName: '',
        taxId: '',
        erpCompanyCode: 'ACME01',
      },
    ]);
  });

  it('read-only: every field disabled and Save disabled', async () => {
    const { fixture, form } = await setup('edit', party, true);

    expect(Object.values(form.form.controls).every((control) => control.disabled)).toBe(true);
    expect((root(fixture).querySelector('[data-testid="party-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });
});
