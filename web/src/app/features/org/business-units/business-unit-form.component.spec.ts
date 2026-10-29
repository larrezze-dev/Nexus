import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { BusinessUnitFormComponent, BusinessUnitFormValue } from './business-unit-form.component';

const legalEntity: LegalEntityListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: legalEntity.id,
  code: 'BU-01',
  captionI18n: { de: 'Vermietung', en: 'Rental' },
  costCenterCode: 'CC-100',
  isActive: true,
};

function createFixture(): ComponentFixture<BusinessUnitFormComponent> {
  return TestBed.createComponent(BusinessUnitFormComponent);
}

describe('BusinessUnitFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        BusinessUnitFormComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              org: { businessUnits: { fields: {} } },
              common: { save: 'Save' },
              shared: {
                entityPicker: { placeholder: 'Search…', noResults: 'No results' },
                i18nCaptionEditor: {
                  groupLabel: 'Caption',
                  deRequired: 'German is required',
                  language: { de: 'German', en: 'English', fr: 'French', it: 'Italian', es: 'Spanish' },
                },
              },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  function setRequiredInputs(fixture: ComponentFixture<BusinessUnitFormComponent>) {
    fixture.componentRef.setInput('legalEntitySearch', () => of<LegalEntityListItem[]>([]));
  }

  it('shows the entity picker in create mode and the read-only display in edit mode', () => {
    const fixture = createFixture();
    setRequiredInputs(fixture);
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="bu-form-legal-entity-picker"]')).toBeTruthy();

    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initial', businessUnit);
    fixture.componentRef.setInput('legalEntityDisplay', 'LE-CH');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="bu-form-legal-entity-picker"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="bu-form-legal-entity-display"]').value).toBe(
      'LE-CH',
    );
  });

  it('sets legalEntityId when an entity is picked, and emits it on save', () => {
    const fixture = createFixture();
    setRequiredInputs(fixture);
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();

    fixture.componentInstance['onLegalEntitySelected'](legalEntity);

    const setValue = (testId: string, value: string) => {
      const input: HTMLInputElement = fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    setValue('bu-form-legal-name', 'Vermietung CH');
    setValue('bu-form-code', 'BU-01');

    (fixture.componentInstance as any).form.controls.captionI18n.setValue({ de: 'Vermietung' });
    fixture.detectChanges();

    const emitted: BusinessUnitFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([
      {
        legalName: 'Vermietung CH',
        displayName: '',
        legalEntityId: legalEntity.id,
        code: 'BU-01',
        captionI18n: { de: 'Vermietung' },
        costCenterCode: '',
      },
    ]);
  });

  it('does not emit save when the caption has no German text', () => {
    const fixture = createFixture();
    setRequiredInputs(fixture);
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();

    fixture.componentInstance['onLegalEntitySelected'](legalEntity);
    const setValue = (testId: string, value: string) => {
      const input: HTMLInputElement = fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    setValue('bu-form-legal-name', 'Vermietung CH');
    setValue('bu-form-code', 'BU-01');

    const emitted: BusinessUnitFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([]);
  });
});
