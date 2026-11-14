import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { NxI18nCaptionEditorComponent } from './nx-i18n-caption-editor.component';
import { I18nText, emptyI18nText } from '../i18n-text.model';

@Component({
  standalone: true,
  imports: [ReactiveFormsModule, NxI18nCaptionEditorComponent],
  template: `
    <form [formGroup]="form">
      <nx-i18n-caption-editor formControlName="captionI18n" />
    </form>
  `,
})
class HostComponent {
  readonly form = new FormGroup({
    captionI18n: new FormControl<I18nText>(emptyI18nText(), { nonNullable: true }),
  });
}

describe('NxI18nCaptionEditorComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        HostComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              shared: {
                i18nCaptionEditor: {
                  groupLabel: 'Caption',
                  deRequired: 'German is required',
                  moreLanguages: 'Show more languages',
                  fewerLanguages: 'Show fewer languages',
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

    fixture = TestBed.createComponent(HostComponent);
  });

  function inputFor(lang: string): HTMLInputElement {
    return fixture.nativeElement.querySelector(`input[data-lang="${lang}"]`);
  }

  it('shows «German is required» once the host form marks every field as touched (invalid submit)', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="nx-i18n-caption-editor-de-error"]')).toBeNull();

    fixture.componentInstance.form.markAllAsTouched();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="nx-i18n-caption-editor-de-error"]')?.textContent).toContain(
      'German is required',
    );
  });

  it('renders one row per supported language', () => {
    fixture.detectChanges();

    expect(inputFor('de')).toBeTruthy();
    expect(inputFor('en')).toBeTruthy();
    expect(inputFor('fr')).toBeTruthy();
    expect(inputFor('it')).toBeTruthy();
    expect(inputFor('es')).toBeTruthy();
  });

  it('is invalid when `de` is empty, even if other languages are filled in', () => {
    fixture.componentInstance.form.setValue({ captionI18n: { de: '', en: 'Rental' } as I18nText });
    fixture.detectChanges();

    expect(fixture.componentInstance.form.get('captionI18n')?.invalid).toBe(true);
  });

  it('becomes valid once `de` has a value', () => {
    fixture.componentInstance.form.setValue({ captionI18n: { de: 'Vermietung', en: 'Rental' } });
    fixture.detectChanges();

    expect(fixture.componentInstance.form.get('captionI18n')?.valid).toBe(true);
  });

  it('writes an externally-set value into the per-language inputs', () => {
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ captionI18n: { de: 'Bühne', fr: 'Scène' } });
    fixture.detectChanges();

    expect(inputFor('de').value).toBe('Bühne');
    expect(inputFor('fr').value).toBe('Scène');
    expect(inputFor('en').value).toBe('');
  });

  it('propagates typed input back out through valueChanges, omitting blank optional languages', () => {
    fixture.detectChanges();

    const emitted: I18nText[] = [];
    fixture.componentInstance.form.get('captionI18n')?.valueChanges.subscribe((value) => emitted.push(value));

    const deInput = inputFor('de');
    deInput.value = 'Vermietung';
    deInput.dispatchEvent(new Event('input'));

    const enInput = inputFor('en');
    enInput.value = 'Rental';
    enInput.dispatchEvent(new Event('input'));

    expect(emitted.at(-1)).toEqual({ de: 'Vermietung', en: 'Rental' });
  });

  it('looks read-only (.nx-readonly, like a locked field) while the host disables it', () => {
    fixture.detectChanges();
    const group = (): HTMLElement => fixture.nativeElement.querySelector('.nx-i18n-caption-editor');
    expect(group().classList).not.toContain('nx-readonly');

    fixture.componentInstance.form.controls.captionI18n.disable();
    fixture.detectChanges();
    expect(group().classList).toContain('nx-readonly');
    expect(inputFor('de').disabled).toBe(true);

    fixture.componentInstance.form.controls.captionI18n.enable();
    fixture.detectChanges();
    expect(group().classList).not.toContain('nx-readonly');
  });

  describe('«Show more languages» (fr/it/es folded below 600 px)', () => {
    function toggle(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('[data-testid="nx-i18n-caption-editor-toggle"]');
    }
    function group(): HTMLElement {
      return fixture.nativeElement.querySelector('.nx-i18n-caption-editor');
    }
    function extraRows(): string[] {
      return [...fixture.nativeElement.querySelectorAll('.nx-i18n-caption-editor__row--extra input')].map(
        (input) => (input as HTMLInputElement).dataset['lang'] ?? '',
      );
    }

    it('folds fr/it/es only, behind a disclosure button placed before them', () => {
      fixture.detectChanges();
      expect(extraRows()).toEqual(['fr', 'it', 'es']);
      expect(toggle().getAttribute('aria-expanded')).toBe('false');
      expect(toggle().textContent).toContain('Show more languages');
      expect(group().classList).not.toContain('nx-i18n-caption-editor--expanded');
      // Controls the three folded fields and comes right before French in the DOM (tab order).
      const ids = toggle().getAttribute('aria-controls')?.split(' ') ?? [];
      expect(ids.map((id) => document.getElementById(id)?.querySelector('input')?.dataset['lang'])).toEqual([
        'fr',
        'it',
        'es',
      ]);
      expect(toggle().nextElementSibling?.querySelector('input')?.dataset['lang']).toBe('fr');
    });

    it('opens and closes on click', () => {
      fixture.detectChanges();
      toggle().click();
      fixture.detectChanges();
      expect(toggle().getAttribute('aria-expanded')).toBe('true');
      expect(toggle().textContent).toContain('Show fewer languages');
      expect(group().classList).toContain('nx-i18n-caption-editor--expanded');

      toggle().click();
      fixture.detectChanges();
      expect(toggle().getAttribute('aria-expanded')).toBe('false');
    });

    it('opens by itself when one of the folded languages already has a value', () => {
      fixture.componentInstance.form.setValue({ captionI18n: { de: 'Bühne', en: 'Stage' } });
      fixture.detectChanges();
      expect(toggle().getAttribute('aria-expanded')).toBe('false');

      fixture.componentInstance.form.setValue({ captionI18n: { de: 'Bühne', it: 'Palco' } });
      fixture.detectChanges();
      expect(toggle().getAttribute('aria-expanded')).toBe('true');
    });
  });
});
