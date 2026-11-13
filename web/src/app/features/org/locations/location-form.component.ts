import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Location } from '../../../api/platform/models/location';
import { countryName, countryOptions } from '../../../shared/ui/country/country-names';
import {
  NxActionBarComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';

/** Form output, mode-agnostic: create and update take the same fields. Empty optional text = `null`. */
export interface LocationFormValue {
  readonly name: string;
  readonly code: string | null;
  readonly street: string | null;
  readonly postalCode: string | null;
  readonly city: string | null;
  readonly region: string | null;
  readonly countryCode: string;
}

/** Controls in the order they appear on screen (the first invalid one gets the focus). */
const FIELD_ORDER = ['name', 'code', 'street', 'postalCode', 'city', 'region', 'countryCode'] as const;

/**
 * Location form (`conventions-frontend.md §4.5`, form template), two sections on the 12-column
 * grid and the action bar (Cancel · Save changes / Create location):
 * - «Identification»: name (required) and the optional code.
 * - «Address»: street, postal code, city, region and country (a select showing the country
 *   name in the UI language, `shared/ui/country`; required).
 *
 * Every field can be changed after creation (`UpdateLocationRequest` carries them all), so
 * there are no locked fields. On an invalid submit: an alert with the number of fields to
 * check, focus on the first one.
 */
@Component({
  selector: 'nx-location-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoModule,
    NxActionBarComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxSpanDirective,
  ],
  templateUrl: './location-form.component.html',
  styles: `
    .nx-location-form {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      min-width: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<Location | null>(null);
  readonly saving = input<boolean>(false);
  /** No permission to change it: every field read-only and Save disabled. */
  readonly readOnly = input<boolean>(false);
  /** Field name (as returned by the `Problem.errors` 400 body) → translated/translatable messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  readonly save = output<LocationFormValue>();
  readonly cancel = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', Validators.required],
    code: [''],
    street: [''],
    postalCode: [''],
    city: [''],
    region: [''],
    countryCode: ['', Validators.required],
  });

  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  /** Countries by name in the UI language. */
  protected readonly countries = computed(() => countryOptions(this.lang()));

  constructor() {
    effect(() => {
      this.initial();
      this.resetToInitial();
    });

    effect(() => {
      if (this.readOnly()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  /** Back to the loaded values (or empty on create), pristine, without the error alert. */
  resetToInitial(): void {
    const location = this.initial();
    this.form.reset(
      {
        name: location?.name ?? '',
        code: location?.code ?? '',
        street: location?.street ?? '',
        postalCode: location?.postalCode ?? '',
        city: location?.city ?? '',
        region: location?.region ?? '',
        countryCode: location?.countryCode ?? '',
      },
      { emitEvent: false },
    );
    this.invalidCount.set(0);
  }

  protected countryLabel(code: string): string {
    return countryName(code, this.lang());
  }

  protected onSubmit(): void {
    if (this.saving() || this.readOnly()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      const invalid = FIELD_ORDER.filter((name) => this.form.controls[name].invalid);
      this.invalidCount.set(invalid.length);
      this.focusField(invalid[0]);
      return;
    }
    this.invalidCount.set(0);
    const raw = this.form.getRawValue();
    this.save.emit({
      name: raw.name.trim(),
      code: optional(raw.code),
      street: optional(raw.street),
      postalCode: optional(raw.postalCode),
      city: optional(raw.city),
      region: optional(raw.region),
      countryCode: raw.countryCode.toUpperCase(),
    });
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }

  private focusField(name: string | undefined): void {
    if (name) {
      this.host.nativeElement.querySelector<HTMLElement>(`[formcontrolname="${name}"]`)?.focus();
    }
  }
}

/** Trimmed text, or `null` when empty (the API's optional fields are nullable). */
function optional(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}
