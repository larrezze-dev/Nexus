import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { Party } from '../../../api/platform/models/party';
import { LegalEntityFormComponent, LegalEntityFormValue } from './legal-entity-form.component';

const legalEntity: LegalEntity = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c11',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const party: Party = {
  id: legalEntity.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud AG',
  displayName: 'SSB Schweiz',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: legalEntity.createdAt,
  updatedAt: legalEntity.updatedAt,
};

const groups = [{ id: legalEntity.groupId, code: 'SAPE', name: 'SAPE Holding AG' }];
const currencies = [
  { code: 'CHF', captionI18n: { de: 'Schweizer Franken', en: 'Swiss franc' }, isActive: true, minorUnits: 2 },
  { code: 'EUR', captionI18n: { de: 'Euro', en: 'Euro' }, isActive: true, minorUnits: 2 },
];

const en = {
  org: {
    legalEntities: {
      fields: { legalName: 'Legal name', displayName: 'Short name', code: 'Code', groupId: 'Group', countryCode: 'Country', currencyCode: 'Currency' },
      sections: { company: 'Company', countryCurrency: 'Country and currency' },
      hints: { legalName: 'Official name', displayName: 'For lists', lockedAfterCreate: 'Set on create' },
      actions: { create: 'Create legal entity' },
    },
  },
  shared: { form: { required: 'Required field', reviewFields: 'Check the {{count}} fields.', reviewFieldsOne: 'Check the field.' } },
  common: { cancel: 'Cancel', saveChanges: 'Save changes' },
};

function root(fixture: ComponentFixture<LegalEntityFormComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function setup(mode: 'create' | 'edit', extra: Partial<{ initial: LegalEntity; party: Party; readOnly: boolean }> = {}) {
  const fixture = TestBed.createComponent(LegalEntityFormComponent);
  fixture.componentRef.setInput('mode', mode);
  fixture.componentRef.setInput('groups', groups);
  fixture.componentRef.setInput('currencies', currencies);
  for (const [key, value] of Object.entries(extra)) {
    fixture.componentRef.setInput(key, value);
  }
  fixture.detectChanges();
  return fixture;
}

describe('LegalEntityFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        LegalEntityFormComponent,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  it('groups the fields in two sections on the form grid, with a span on every field', () => {
    const fixture = setup('create');
    const sections = root(fixture).querySelectorAll('nx-form-section');

    expect(Array.from(sections).map((section) => section.querySelector('h2')?.textContent?.trim())).toEqual([
      'Company',
      'Country and currency',
    ]);
    const fields = root(fixture).querySelectorAll('nx-form-grid > mat-form-field');
    expect(fields.length).toBe(6);
    fields.forEach((field) => expect(field.className).toMatch(/nx-span--(s|m|l|xl)/));
    expect(root(fixture).querySelector('nx-action-bar [data-testid="le-form-submit"]')?.textContent).toContain(
      'Create legal entity',
    );
  });

  it('in edit mode shows legal name, short name and group read-only with a lock and the reason', () => {
    const fixture = setup('edit', { initial: legalEntity, party });

    const legalName = root(fixture).querySelector('[data-testid="le-form-legal-name"]') as HTMLInputElement;
    expect(legalName.value).toBe('SAPE smart bud AG');
    expect(legalName.disabled).toBe(true);
    expect((root(fixture).querySelector('[data-testid="le-form-display-name"]') as HTMLInputElement).value).toBe('SSB Schweiz');
    expect(root(fixture).querySelectorAll('.nx-le-form__lock').length).toBe(3);
    expect(root(fixture).textContent).toContain('Set on create');
    expect((root(fixture).querySelector('[data-testid="le-form-code"]') as HTMLInputElement).value).toBe('LE-CH');
    expect((root(fixture).querySelector('[data-testid="le-form-code"]') as HTMLInputElement).disabled).toBe(false);
  });

  it('shows code and name of the current value inside the selects (no «Current: …» text)', async () => {
    const fixture = setup('edit', { initial: legalEntity, party });
    await fixture.whenStable();
    fixture.detectChanges();
    const text = (testId: string) =>
      root(fixture).querySelector(`[data-testid="${testId}"] .mat-mdc-select-value`)?.textContent?.replace(/\s+/g, ' ').trim();

    expect(text('le-form-group')).toBe('SAPE SAPE Holding AG');
    expect(text('le-form-currency')).toBe('CHF Swiss franc');
    expect(text('le-form-country')).toBe('Switzerland (CH)');
  });

  it('keeps Save disabled until something changes in edit mode', () => {
    const fixture = setup('edit', { initial: legalEntity, party });
    const submit = root(fixture).querySelector('[data-testid="le-form-submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);

    const code = root(fixture).querySelector('[data-testid="le-form-code"]') as HTMLInputElement;
    code.value = 'LE-CH2';
    code.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(submit.disabled).toBe(false);
  });

  it('disables every field and Save when read-only', () => {
    const fixture = setup('edit', { initial: legalEntity, party, readOnly: true });

    expect((root(fixture).querySelector('[data-testid="le-form-code"]') as HTMLInputElement).disabled).toBe(true);
    expect((root(fixture).querySelector('[data-testid="le-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('on an invalid submit shows the number of fields to check, marks them and does not emit', () => {
    const fixture = setup('create');
    const emitted: LegalEntityFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(root(fixture).querySelector('[data-testid="le-form-invalid"]')?.textContent).toContain('Check the 5 fields.');
    expect(root(fixture).querySelectorAll('mat-error').length).toBeGreaterThan(0);
    expect(document.activeElement).toBe(root(fixture).querySelector('[data-testid="le-form-legal-name"]'));
  });

  it('emits the trimmed values once the form is valid', () => {
    const fixture = setup('create');
    const emitted: LegalEntityFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    const form = (fixture.componentInstance as unknown as { form: { setValue(value: object): void } }).form;
    form.setValue({
      legalName: ' Nexus Event AG ',
      displayName: '',
      groupId: legalEntity.groupId,
      code: ' LE-NX ',
      countryCode: 'CH',
      currencyCode: 'CHF',
    });
    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([
      {
        legalName: 'Nexus Event AG',
        displayName: '',
        groupId: legalEntity.groupId,
        code: 'LE-NX',
        countryCode: 'CH',
        currencyCode: 'CHF',
      },
    ]);
  });

  it('emits cancel from the action bar', () => {
    const fixture = setup('create');
    let cancelled = 0;
    fixture.componentInstance.cancel.subscribe(() => cancelled++);

    (root(fixture).querySelector('[data-testid="le-form-cancel"]') as HTMLButtonElement).click();

    expect(cancelled).toBe(1);
  });
});
