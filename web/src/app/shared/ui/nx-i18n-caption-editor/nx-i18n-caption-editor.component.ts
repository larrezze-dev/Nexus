import {
  AfterViewInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  Injector,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormControl,
  FormGroup,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  NgControl,
  ReactiveFormsModule,
  TouchedChangeEvent,
  ValidationErrors,
  Validator,
  Validators,
  type ControlValueAccessor,
} from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoModule } from '@jsverse/transloco';
import { SUPPORTED_LANGUAGES, SupportedLanguage } from '../../../core/i18n/supported-languages';
import { I18nText, emptyI18nText } from '../i18n-text.model';

type CaptionFormGroup = FormGroup<Record<SupportedLanguage, FormControl<string>>>;

/** Languages folded behind «Show more languages» below 600 px (§4.6): de/en stay visible. */
const EXTRA_LANGUAGES: readonly SupportedLanguage[] = ['fr', 'it', 'es'];

let nextId = 0;

/**
 * One row per supported language (`de`, `en`, `fr`, `it`, `es`), `de`
 * mandatory (`conventions-frontend.md §4`). Plugs into any Reactive Form as
 * a real control (`ControlValueAccessor` + `Validator`): a parent
 * `FormGroup` can do `formGroup.get('captionI18n')` and get the usual
 * `value`, `valueChanges`, `setValue`, `invalid`, exactly like any other
 * control — the host form never needs to know this is a composite field.
 *
 * Value shape is `I18nText` (`{ de: string; en?: string; ... }`), matching
 * `BusinessUnit.captionI18n` / `Warehouse.captionI18n` in the OpenAPI
 * contract (`contracts/openapi/_shared/components.yaml#I18nText`).
 *
 * Below 600 px (§4.6, board 7) only de/en are shown; fr/it/es fold behind a «Show more
 * languages» disclosure button (`aria-expanded`), opened by itself when one of them has a value.
 */
@Component({
  selector: 'nx-i18n-caption-editor',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslocoModule,
  ],
  templateUrl: './nx-i18n-caption-editor.component.html',
  styleUrl: './nx-i18n-caption-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => NxI18nCaptionEditorComponent),
      multi: true,
    },
    {
      provide: NG_VALIDATORS,
      useExisting: forwardRef(() => NxI18nCaptionEditorComponent),
      multi: true,
    },
  ],
})
export class NxI18nCaptionEditorComponent implements ControlValueAccessor, Validator, AfterViewInit {
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly firstExtraLanguage = EXTRA_LANGUAGES[0];
  protected readonly idPrefix = `nx-i18n-caption-editor-${nextId++}`;
  /** Ids of the folded fields, for the toggle's `aria-controls`. */
  protected readonly extraIds = EXTRA_LANGUAGES.map((lang) => `${this.idPrefix}-${lang}`).join(' ');
  /** fr/it/es shown below 600 px (always shown above: the fold is CSS only). */
  protected readonly expanded = signal(false);
  /** Disabled by the host form = read-only: legible like a locked field (`.nx-readonly`, components.css). */
  protected readonly disabled = signal(false);

  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  protected readonly form: CaptionFormGroup;

  private onChange: (value: I18nText) => void = () => {};
  private onTouched: () => void = () => {};

  constructor(fb: FormBuilder) {
    this.form = fb.group({
      de: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      en: fb.control('', { nonNullable: true }),
      fr: fb.control('', { nonNullable: true }),
      it: fb.control('', { nonNullable: true }),
      es: fb.control('', { nonNullable: true }),
    });

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.onChange(this.toI18nText(value));
    });
  }

  /**
   * The host form's `markAllAsTouched()` (invalid submit) touches the composite control only;
   * pass it on to the per-language rows so «German is required» shows like any other field error
   * (and clear it again when the host form is reset).
   * `NgControl` is read lazily (a constructor injection would be a DI cycle with the
   * `NG_VALUE_ACCESSOR` this component provides), and only after the view init: `formControlName`
   * binds its control in its own `ngOnChanges`, which runs after this component's `ngOnInit`.
   */
  ngAfterViewInit(): void {
    const control = this.injector.get(NgControl, null, { self: true })?.control;
    control?.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      if (event instanceof TouchedChangeEvent) {
        if (event.touched) {
          this.form.markAllAsTouched();
        } else {
          this.form.markAsUntouched(); // host form reset
        }
        this.cdr.markForCheck();
      }
    });
  }

  writeValue(value: I18nText | null): void {
    const next = value ?? emptyI18nText();
    this.form.patchValue(
      {
        de: next.de ?? '',
        en: next.en ?? '',
        fr: next.fr ?? '',
        it: next.it ?? '',
        es: next.es ?? '',
      },
      { emitEvent: false },
    );
    // A translation already there must not hide behind the fold.
    if (EXTRA_LANGUAGES.some((lang) => !!next[lang]?.trim())) {
      this.expanded.set(true);
    }
  }

  registerOnChange(fn: (value: I18nText) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) {
      this.form.disable({ emitEvent: false });
    } else {
      this.form.enable({ emitEvent: false });
    }
  }

  /** `de` is the reference language (`conventions-api.md §5`): the composite control is invalid without it. */
  validate(_control: AbstractControl): ValidationErrors | null {
    return this.form.controls.de.valid ? null : { i18nCaptionDeRequired: true };
  }

  protected isExtra(lang: SupportedLanguage): boolean {
    return EXTRA_LANGUAGES.includes(lang);
  }

  protected toggleExpanded(): void {
    this.expanded.update((open) => !open);
  }

  protected onBlur(): void {
    this.onTouched();
  }

  private toI18nText(value: Partial<Record<SupportedLanguage, string>>): I18nText {
    const result: Record<string, string> = { de: value.de ?? '' };
    for (const lang of this.languages) {
      if (lang === 'de') {
        continue;
      }
      const translated = value[lang];
      if (translated) {
        result[lang] = translated;
      }
    }
    return result as I18nText;
  }
}
