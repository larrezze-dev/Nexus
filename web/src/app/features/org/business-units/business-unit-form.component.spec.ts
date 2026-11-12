import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { Party } from '../../../api/platform/models/party';
import { BusinessUnitFormComponent, BusinessUnitFormValue } from './business-unit-form.component';
import { BusinessUnitLegalEntityOption } from './business-units.facade';

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-BCN-RD',
  captionI18n: { de: 'F+E Barcelona', es: 'I+D Barcelona' },
  costCenterCode: 'CC-100',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const party: Party = {
  id: businessUnit.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud I+D Barcelona',
  displayName: 'I+D Barcelona',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: businessUnit.createdAt,
  updatedAt: businessUnit.updatedAt,
};

const legalEntities: BusinessUnitLegalEntityOption[] = [
  { id: businessUnit.legalEntityId, code: 'LE-SSB', legalName: 'SAPE smart bud S.L.', isActive: true },
  { id: 'le-old', code: 'LE-OLD', legalName: 'Old company', isActive: false },
];

const en = {
  org: {
    businessUnits: {
      fields: {
        legalName: 'Legal name',
        displayName: 'Short name',
        legalEntityId: 'Legal entity',
        code: 'Code',
        costCenterCode: 'Cost center',
      },
      sections: { identification: 'Identification', names: 'Name in each language', party: 'Party record' },
      sectionHints: { names: 'German is required.', party: 'Also an internal party.' },
      hints: { legalEntity: 'Owner', costCenterCode: 'Accounting', displayName: 'For lists', lockedAfterCreate: 'Set on create' },
      actions: { create: 'Create business unit' },
    },
  },
  shared: {
    form: { required: 'Required field', reviewFields: 'Check the {{count}} fields.', reviewFieldsOne: 'Check the field.' },
    i18nCaptionEditor: { language: { de: 'German', en: 'English', fr: 'French', it: 'Italian', es: 'Spanish' }, deRequired: 'German is required' },
  },
  common: { cancel: 'Cancel', saveChanges: 'Save changes' },
};

function root(fixture: ComponentFixture<BusinessUnitFormComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function setup(
  mode: 'create' | 'edit',
  extra: Partial<{ initial: BusinessUnit; party: Party | null; readOnly: boolean }> = {},
) {
  const fixture = TestBed.createComponent(BusinessUnitFormComponent);
  fixture.componentRef.setInput('mode', mode);
  fixture.componentRef.setInput('legalEntities', legalEntities);
  for (const [key, value] of Object.entries(extra)) {
    fixture.componentRef.setInput(key, value);
  }
  fixture.detectChanges();
  return fixture;
}

function input(fixture: ComponentFixture<BusinessUnitFormComponent>, testId: string): HTMLInputElement {
  return root(fixture).querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
}

describe('BusinessUnitFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        BusinessUnitFormComponent,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  it('groups the fields in three sections on the form grid, with a span on every field', () => {
    const fixture = setup('create');
    const sections = root(fixture).querySelectorAll('nx-form-section');

    expect(Array.from(sections).map((section) => section.querySelector('h2')?.textContent?.trim())).toEqual([
      'Identification',
      'Name in each language',
      'Party record',
    ]);
    const fields = root(fixture).querySelectorAll('nx-form-grid > mat-form-field, nx-form-grid > nx-i18n-caption-editor');
    expect(fields.length).toBe(6);
    fields.forEach((field) => expect(field.className).toMatch(/nx-span--(s|m|l|xl)/));
    expect(root(fixture).querySelector('nx-action-bar [data-testid="bu-form-submit"]')?.textContent).toContain(
      'Create business unit',
    );
  });

  it('on create offers only the active legal entities, by code and legal name', async () => {
    const fixture = setup('create');
    (root(fixture).querySelector('[data-testid="bu-form-legal-entity"] .mat-mdc-select-trigger') as HTMLElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    const options = Array.from(document.querySelectorAll('mat-option')).map((option) =>
      option.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(options).toEqual(['LE-SSB SAPE smart bud S.L.']);
  });

  it('in edit mode shows the legal entity and the party names read-only with a lock and the reason', async () => {
    const fixture = setup('edit', { initial: businessUnit, party });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(
      root(fixture)
        .querySelector('[data-testid="bu-form-legal-entity"] .mat-mdc-select-value')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim(),
    ).toBe('LE-SSB SAPE smart bud S.L.');
    expect(input(fixture, 'bu-form-legal-name').value).toBe('SAPE smart bud I+D Barcelona');
    expect(input(fixture, 'bu-form-legal-name').disabled).toBe(true);
    expect(input(fixture, 'bu-form-display-name').value).toBe('I+D Barcelona');
    expect(root(fixture).querySelectorAll('.nx-bu-form__lock').length).toBe(3);
    expect(root(fixture).textContent).toContain('Set on create');
    expect(input(fixture, 'bu-form-code').value).toBe('BU-BCN-RD');
    expect(input(fixture, 'bu-form-code').disabled).toBe(false);
    expect((root(fixture).querySelector('input[data-lang="es"]') as HTMLInputElement).value).toBe('I+D Barcelona');
  });

  it('in edit mode leaves out the party section when the party could not be read', () => {
    const fixture = setup('edit', { initial: businessUnit, party: null });

    expect(root(fixture).querySelectorAll('nx-form-section').length).toBe(2);
    expect(input(fixture, 'bu-form-legal-name')).toBeNull();
  });

  it('keeps Save disabled until something changes in edit mode', () => {
    const fixture = setup('edit', { initial: businessUnit, party });
    const submit = root(fixture).querySelector('[data-testid="bu-form-submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);

    const fr = root(fixture).querySelector('input[data-lang="fr"]') as HTMLInputElement;
    fr.value = 'R&D Barcelone';
    fr.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(submit.disabled).toBe(false);
  });

  it('disables every field, the captions included, and Save when read-only', () => {
    const fixture = setup('edit', { initial: businessUnit, party, readOnly: true });

    expect(input(fixture, 'bu-form-code').disabled).toBe(true);
    expect(input(fixture, 'bu-form-cost-center').disabled).toBe(true);
    expect((root(fixture).querySelector('input[data-lang="de"]') as HTMLInputElement).disabled).toBe(true);
    expect((root(fixture).querySelector('[data-testid="bu-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('on an invalid submit counts the fields to check (German name included), marks them and focuses the first', () => {
    const fixture = setup('create');
    const emitted: BusinessUnitFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    // code, legal entity, German name, legal name
    expect(root(fixture).querySelector('[data-testid="bu-form-invalid"]')?.textContent).toContain('Check the 4 fields.');
    expect(root(fixture).querySelector('[data-testid="nx-i18n-caption-editor-de-error"]')?.textContent).toContain(
      'German is required',
    );
    expect(document.activeElement).toBe(input(fixture, 'bu-form-code'));
  });

  it('emits the trimmed values once the form is valid', () => {
    const fixture = setup('create');
    const emitted: BusinessUnitFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    const form = (fixture.componentInstance as unknown as { form: { setValue(value: object): void } }).form;
    form.setValue({
      code: ' BU-NEW ',
      legalEntityId: businessUnit.legalEntityId,
      costCenterCode: ' CC-1 ',
      captionI18n: { de: 'Vermietung' },
      legalName: ' SAPE smart bud Rental ',
      displayName: '',
    });
    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([
      {
        code: 'BU-NEW',
        legalEntityId: businessUnit.legalEntityId,
        costCenterCode: 'CC-1',
        captionI18n: { de: 'Vermietung' },
        legalName: 'SAPE smart bud Rental',
        displayName: '',
      },
    ]);
  });

  it('emits cancel from the action bar', () => {
    const fixture = setup('create');
    let cancelled = 0;
    fixture.componentInstance.cancel.subscribe(() => cancelled++);

    (root(fixture).querySelector('[data-testid="bu-form-cancel"]') as HTMLButtonElement).click();

    expect(cancelled).toBe(1);
  });
});
