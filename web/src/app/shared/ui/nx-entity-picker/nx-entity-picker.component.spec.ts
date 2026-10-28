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
});
