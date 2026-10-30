import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
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

function createFixture(): ComponentFixture<LegalEntityFormComponent> {
  return TestBed.createComponent(LegalEntityFormComponent);
}

describe('LegalEntityFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        LegalEntityFormComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { org: { legalEntities: { fields: {} } }, common: { save: 'Save' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  it('shows legalName/displayName/groupId only in create mode', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="le-form-legal-name"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="le-form-group-id"]')).toBeTruthy();
  });

  it('hides legalName/displayName/groupId in edit mode', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('mode', 'edit');
    fixture.componentRef.setInput('initial', legalEntity);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="le-form-legal-name"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="le-form-group-id"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="le-form-code"]').value).toBe('LE-CH');
  });

  it('does not emit save when required fields are missing', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();

    const emitted: LegalEntityFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([]);
  });

  it('emits save with the uppercased currency/country codes once the form is valid', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('mode', 'create');
    fixture.detectChanges();

    const setValue = (testId: string, value: string) => {
      const input: HTMLInputElement = fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    setValue('le-form-legal-name', 'Nexus Event AG');
    setValue('le-form-group-id', '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30');
    setValue('le-form-code', 'LE-CH');
    setValue('le-form-currency-code', 'chf');
    setValue('le-form-country-code', 'ch');

    const emitted: LegalEntityFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([
      {
        legalName: 'Nexus Event AG',
        displayName: '',
        groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
        code: 'LE-CH',
        currencyCode: 'CHF',
        countryCode: 'CH',
      },
    ]);
  });
});
