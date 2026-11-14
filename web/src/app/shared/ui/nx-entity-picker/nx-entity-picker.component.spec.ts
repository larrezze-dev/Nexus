import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { Subject, of, throwError } from 'rxjs';
import { NxEntityPickerComponent } from './nx-entity-picker.component';

interface Item {
  readonly id: string;
  readonly name: string;
}

const displayWith = (item: Item): string => item.name;

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function createFixture(): ComponentFixture<NxEntityPickerComponent<Item>> {
  return TestBed.createComponent(NxEntityPickerComponent<Item>);
}

describe('NxEntityPickerComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        NxEntityPickerComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { shared: { entityPicker: { placeholder: 'Search…', noResults: 'No results' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideNoopAnimations()],
    }).compileComponents();
  });

  it('debounces keystrokes so a fast burst only triggers one search, with the latest query', async () => {
    const searchSpy = vi.fn((_query: string) => of<Item[]>([]));
    const fixture = createFixture();
    fixture.componentRef.setInput('search', searchSpy);
    fixture.componentRef.setInput('displayWith', displayWith);
    fixture.componentRef.setInput('debounceMs', 20);
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-input"]');
    for (const partial of ['a', 'ab', 'abc']) {
      input.value = partial;
      input.dispatchEvent(new Event('input'));
    }
    expect(searchSpy).not.toHaveBeenCalled();

    await delay(60);

    expect(searchSpy).toHaveBeenCalledTimes(1);
    expect(searchSpy).toHaveBeenCalledWith('abc');
  });

  it('shows a loading state while the search is in flight and populates options once it resolves', async () => {
    const resultSubject = new Subject<Item[]>();
    const fixture = createFixture();
    fixture.componentRef.setInput('search', () => resultSubject.asObservable());
    fixture.componentRef.setInput('displayWith', displayWith);
    fixture.componentRef.setInput('debounceMs', 1);
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-input"]');
    input.value = 'leg';
    input.dispatchEvent(new Event('input'));

    await delay(10);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-loading"]')).toBeTruthy();

    resultSubject.next([{ id: '1', name: 'Legal Entity A' }]);
    resultSubject.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-loading"]')).toBeFalsy();
    // The `mat-option`s only render into the DOM once the CDK overlay panel is open; the
    // component's own `options` signal is the source of truth tested here directly.
    expect((fixture.componentInstance as any).options()).toEqual([{ id: '1', name: 'Legal Entity A' }]);
  });

  it('emits the selected item', () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('search', () => of<Item[]>([]));
    fixture.componentRef.setInput('displayWith', displayWith);
    fixture.detectChanges();

    const emitted: Item[] = [];
    fixture.componentInstance.selected.subscribe((item) => emitted.push(item));

    const item: Item = { id: '2', name: 'Legal Entity B' };
    const fakeEvent = { option: { value: item } } as unknown as MatAutocompleteSelectedEvent;
    (fixture.componentInstance as any).onOptionSelected(fakeEvent);

    expect(emitted).toEqual([item]);
  });

  it('recovers to an empty option list if the search errors', async () => {
    const fixture = createFixture();
    fixture.componentRef.setInput('search', () => throwError(() => new Error('boom')));
    fixture.componentRef.setInput('displayWith', displayWith);
    fixture.componentRef.setInput('debounceMs', 1);
    fixture.detectChanges();

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-input"]');
    input.value = 'x';
    input.dispatchEvent(new Event('input'));

    await delay(10);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-entity-picker-loading"]')).toBeFalsy();
    expect((fixture.componentInstance as any).options()).toEqual([]);
  });

  describe('as a form field', () => {
    function field(value: Item | null = null): ComponentFixture<NxEntityPickerComponent<Item>> {
      const fixture = createFixture();
      fixture.componentRef.setInput('search', () => of<Item[]>([]));
      fixture.componentRef.setInput('displayWith', displayWith);
      fixture.componentRef.setInput('label', 'Business unit');
      fixture.componentRef.setInput('hint', 'Optional');
      fixture.componentRef.setInput('clearable', true);
      fixture.componentRef.setInput('clearLabel', 'Remove business unit');
      fixture.componentRef.setInput('value', value);
      fixture.detectChanges();
      return fixture;
    }
    const el = (fixture: ComponentFixture<unknown>, selector: string) =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(selector);

    it('shows the label inside the field, the hint, and the chosen item as text', async () => {
      const fixture = field({ id: '1', name: 'BU-CH Events' });
      await fixture.whenStable(); // the autocomplete writes the text in a microtask

      expect(el(fixture, 'mat-label')?.textContent).toContain('Business unit');
      expect(el(fixture, 'mat-hint')?.textContent).toContain('Optional');
      expect((el(fixture, '[data-testid="nx-entity-picker-input"]') as HTMLInputElement).value).toBe('BU-CH Events');
    });

    it('the «×» has an accessible name and emits cleared; no «×» without a value', async () => {
      const fixture = field({ id: '1', name: 'BU-CH Events' });
      const cleared = vi.fn();
      fixture.componentInstance.cleared.subscribe(cleared);

      const clear = el(fixture, '[data-testid="nx-entity-picker-clear"]');
      expect(clear?.getAttribute('aria-label')).toBe('Remove business unit');
      clear?.click();
      expect(cleared).toHaveBeenCalledTimes(1);

      fixture.componentRef.setInput('value', null);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(el(fixture, '[data-testid="nx-entity-picker-clear"]')).toBeNull();
      expect((el(fixture, '[data-testid="nx-entity-picker-input"]') as HTMLInputElement).value).toBe('');
    });

    it('shows the error under the field and marks it invalid while set', () => {
      const fixture = field();
      fixture.componentRef.setInput('error', 'Required field');
      fixture.detectChanges();

      expect(el(fixture, '[data-testid="nx-entity-picker-error"]')?.textContent).toContain('Required field');
      expect(el(fixture, 'mat-form-field')?.classList).toContain('mat-form-field-invalid');
    });

    it('closing the list after typing puts the chosen item back', async () => {
      const item = { id: '1', name: 'BU-CH Events' };
      const fixture = field(item);
      const input = el(fixture, '[data-testid="nx-entity-picker-input"]') as HTMLInputElement;
      input.value = 'zz';
      input.dispatchEvent(new Event('input'));

      (fixture.componentInstance as any).onClosed();
      await fixture.whenStable();

      expect(input.value).toBe('BU-CH Events');
    });
  });
});
