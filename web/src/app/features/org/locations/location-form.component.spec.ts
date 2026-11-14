import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Location } from '../../../api/platform/models/location';
import { LocationFormComponent, LocationFormValue } from './location-form.component';

const location: Location = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  code: 'LOC-CH-01',
  name: 'Lager Zug',
  street: null,
  postalCode: '6300',
  city: 'Zug',
  region: null,
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const en = {
  org: {
    locations: {
      fields: {
        code: 'Code',
        name: 'Name',
        street: 'Street and number',
        postalCode: 'Postal code',
        city: 'City',
        region: 'Region',
        countryCode: 'Country',
      },
      sections: { identification: 'Identification', address: 'Address' },
      hints: { name: 'Known as', code: 'Optional', region: 'Canton' },
      actions: { create: 'Create location' },
    },
  },
  shared: {
    form: { required: 'Required field', reviewFields: 'Check the {{count}} fields.', reviewFieldsOne: 'Check the field.' },
  },
  common: { cancel: 'Cancel', saveChanges: 'Save changes' },
};

function root(fixture: ComponentFixture<LocationFormComponent>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function setup(mode: 'create' | 'edit', extra: Partial<{ initial: Location; readOnly: boolean }> = {}) {
  const fixture = TestBed.createComponent(LocationFormComponent);
  fixture.componentRef.setInput('mode', mode);
  for (const [key, value] of Object.entries(extra)) {
    fixture.componentRef.setInput(key, value);
  }
  fixture.detectChanges();
  return fixture;
}

function input(fixture: ComponentFixture<LocationFormComponent>, testId: string): HTMLInputElement {
  return root(fixture).querySelector(`[data-testid="${testId}"]`) as HTMLInputElement;
}

function submit(fixture: ComponentFixture<LocationFormComponent>): HTMLButtonElement {
  return root(fixture).querySelector('[data-testid="location-form-submit"]') as HTMLButtonElement;
}

describe('LocationFormComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        LocationFormComponent,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  it('groups the fields in Identification and Address on the form grid, with a span on every field', () => {
    const fixture = setup('create');
    const sections = root(fixture).querySelectorAll('nx-form-section');

    expect(Array.from(sections).map((section) => section.querySelector('h2')?.textContent?.trim())).toEqual([
      'Identification',
      'Address',
    ]);
    const fields = root(fixture).querySelectorAll('nx-form-grid > mat-form-field');
    expect(fields.length).toBe(7);
    fields.forEach((field) => expect(field.className).toMatch(/nx-span--(s|m|l|xl)/));
    expect(root(fixture).querySelector('nx-action-bar [data-testid="location-form-submit"]')?.textContent).toContain(
      'Create location',
    );
  });

  it('shows the country by name, with its code, inside the field', async () => {
    const fixture = setup('edit', { initial: location });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(
      root(fixture)
        .querySelector('[data-testid="location-form-country"] .mat-mdc-select-value')
        ?.textContent?.replace(/\s+/g, ' ')
        .trim(),
    ).toBe('Switzerland (CH)');
    expect(input(fixture, 'location-form-name').value).toBe('Lager Zug');
    expect(input(fixture, 'location-form-postal-code').value).toBe('6300');
    expect(input(fixture, 'location-form-street').value).toBe('');
  });

  it('keeps Save disabled until something changes in edit mode, and every field stays editable', () => {
    const fixture = setup('edit', { initial: location });
    expect(submit(fixture).disabled).toBe(true);
    expect(root(fixture).querySelector('.nx-field--locked')).toBeNull();

    const street = input(fixture, 'location-form-street');
    street.value = 'Industriestrasse 1';
    street.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(submit(fixture).disabled).toBe(false);
  });

  it('disables every field and Save when read-only', () => {
    const fixture = setup('edit', { initial: location, readOnly: true });

    expect(input(fixture, 'location-form-name').disabled).toBe(true);
    expect(input(fixture, 'location-form-city').disabled).toBe(true);
    expect(submit(fixture).disabled).toBe(true);
  });

  it('on an invalid submit counts the fields to check, marks them and focuses the first', () => {
    const fixture = setup('create');
    const emitted: LocationFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    // name and country
    expect(root(fixture).querySelector('[data-testid="location-form-invalid"]')?.textContent).toContain(
      'Check the 2 fields.',
    );
    expect(root(fixture).querySelectorAll('mat-error').length).toBe(2);
    expect(document.activeElement).toBe(input(fixture, 'location-form-name'));
  });

  it('emits trimmed values, empty optional fields as null', () => {
    const fixture = setup('create');
    const emitted: LocationFormValue[] = [];
    fixture.componentInstance.save.subscribe((value) => emitted.push(value));

    const form = (fixture.componentInstance as unknown as { form: { setValue(value: object): void } }).form;
    form.setValue({
      name: ' Lager Zug ',
      code: '  ',
      street: ' Industriestrasse 1 ',
      postalCode: '6300',
      city: 'Zug',
      region: '',
      countryCode: 'CH',
    });
    root(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(emitted).toEqual([
      {
        name: 'Lager Zug',
        code: null,
        street: 'Industriestrasse 1',
        postalCode: '6300',
        city: 'Zug',
        region: null,
        countryCode: 'CH',
      },
    ]);
  });

  it('emits cancel from the action bar', () => {
    const fixture = setup('create');
    let cancelled = 0;
    fixture.componentInstance.cancel.subscribe(() => cancelled++);

    (root(fixture).querySelector('[data-testid="location-form-cancel"]') as HTMLButtonElement).click();

    expect(cancelled).toBe(1);
  });
});
