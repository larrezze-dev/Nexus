import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule } from '@jsverse/transloco';
import { Currency } from '../../../api/platform/models/currency';
import { markServerErrors } from '../../../shared/forms/server-errors';
import {
  NxActionBarComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { I18nText, emptyI18nText } from '../../../shared/ui/i18n-text.model';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';

/** ISO 4217 code as the contract requires it: exactly three capital letters. */
export const CURRENCY_CODE_PATTERN = /^[A-Z]{3}$/;

/** Whole numbers only: 2.5 decimals makes no sense. */
const integerValidator: ValidatorFn = (control) =>
  Number.isInteger(Number(control.value)) ? null : { integer: true };

/** Form output: on edit only `captionI18n` matters (code and decimals are fixed after creation). */
export interface CurrencyFormValue {
  readonly code: string;
  readonly captionI18n: I18nText;
  readonly minorUnits: number;
}

/** Controls in the order they appear on screen (the first invalid one gets the focus). */
const FIELD_ORDER = ['code', 'minorUnits', 'captionI18n'] as const;

/**
 * Currency form: create (`code` ISO 4217 in capitals, name in each language with German required,
 * decimals 0..4, default 2) and edit (only the name changes; `code` and `minorUnits` are locked).
 * Layout (`nx-ui-page-templates`, like the legal entity form): «Currency» section with the code
 * (mono, capitals) and the decimals, «Name in each language» section with the caption editor, and
 * the action bar «Cancel · Create currency / Save changes». On edit the locked fields keep a lock
 * icon and a hint saying why.
 */
@Component({
  selector: 'nx-currency-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoModule,
    NxActionBarComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxI18nCaptionEditorComponent,
    NxSpanDirective,
  ],
  template: `
    @let isEdit = mode() === 'edit';
    <form class="nx-form-stack" [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
      @if (invalidCount() > 0) {
        <div class="nx-alert nx-alert--error" role="alert" data-testid="currency-form-invalid">
          <mat-icon aria-hidden="true">error</mat-icon>
          <span class="nx-alert__text">{{
            invalidCount() === 1
              ? ('shared.form.reviewFieldsOne' | transloco)
              : ('shared.form.reviewFields' | transloco: { count: invalidCount() })
          }}</span>
        </div>
      }

      <nx-form-section [title]="'org.currencies.sections.currency' | transloco">
        <nx-form-grid>
          <mat-form-field nxSpan="s" subscriptSizing="dynamic" [class.nx-field--locked]="isEdit">
            <mat-label>{{ 'org.currencies.fields.code' | transloco }}</mat-label>
            <input
              matInput
              class="nx-mono"
              formControlName="code"
              maxlength="3"
              autocomplete="off"
              data-testid="currency-form-code"
              [required]="!isEdit"
              (input)="onCodeInput($event)"
            />
            <!-- One element per @if block: a block with two roots is not projected into the slots. -->
            @if (isEdit) {
              <mat-icon matSuffix class="nx-currency-form__lock" aria-hidden="true">lock</mat-icon>
            }
            @if (isEdit) {
              <mat-hint>{{ 'org.currencies.hints.lockedAfterCreate' | transloco }}</mat-hint>
            } @else {
              <mat-hint>{{ 'org.currencies.hints.code' | transloco }}</mat-hint>
            }
            @if (form.controls.code.hasError('required')) {
              <mat-error>{{ 'shared.form.required' | transloco }}</mat-error>
            }
            @if (form.controls.code.hasError('pattern')) {
              <mat-error data-testid="currency-form-code-format">{{
                'org.currencies.errors.codeFormat' | transloco
              }}</mat-error>
            }
            @for (message of errorsFor('code'); track message) {
              <mat-error data-testid="currency-form-code-server">{{ message }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field nxSpan="s" subscriptSizing="dynamic" [class.nx-field--locked]="isEdit">
            <mat-label>{{ 'org.currencies.fields.minorUnits' | transloco }}</mat-label>
            <input
              matInput
              type="number"
              min="0"
              max="4"
              step="1"
              formControlName="minorUnits"
              data-testid="currency-form-minor-units"
              [required]="!isEdit"
            />
            <!-- One element per @if block: a block with two roots is not projected into the slots. -->
            @if (isEdit) {
              <mat-icon matSuffix class="nx-currency-form__lock" aria-hidden="true">lock</mat-icon>
            }
            @if (isEdit) {
              <mat-hint>{{ 'org.currencies.hints.lockedAfterCreate' | transloco }}</mat-hint>
            } @else {
              <mat-hint>{{ 'org.currencies.hints.minorUnits' | transloco }}</mat-hint>
            }
            @if (form.controls.minorUnits.invalid) {
              <mat-error data-testid="currency-form-minor-units-range">{{
                'org.currencies.errors.minorUnitsRange' | transloco
              }}</mat-error>
            }
          </mat-form-field>
        </nx-form-grid>
      </nx-form-section>

      <nx-form-section [title]="'org.currencies.sections.names' | transloco">
        <nx-form-grid>
          <nx-i18n-caption-editor
            nxSpan="xl"
            formControlName="captionI18n"
            data-testid="currency-form-caption"
          />
        </nx-form-grid>
      </nx-form-section>

      <nx-action-bar>
        <button mat-button type="button" data-testid="currency-form-cancel" (click)="cancel.emit()">
          {{ 'common.cancel' | transloco }}
        </button>
        <button
          mat-flat-button
          type="submit"
          data-testid="currency-form-submit"
          [disabled]="saving() || readOnly() || (isEdit && form.pristine)"
        >
          @if (saving()) {
            <mat-progress-spinner diameter="18" mode="indeterminate" aria-hidden="true" />
          }
          {{ (isEdit ? 'common.saveChanges' : 'org.currencies.actions.create') | transloco }}
        </button>
      </nx-action-bar>
    </form>
  `,
  styles: `
    .nx-currency-form__lock {
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrencyFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<Currency | null>(null);
  readonly saving = input<boolean>(false);
  /** No permission to change it: every field read-only and Save disabled. */
  readonly readOnly = input<boolean>(false);
  /** Field → messages to show (a 409 `CONFLICT` on the `code`); marks those controls invalid. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  readonly save = output<CurrencyFormValue>();
  readonly cancel = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(CURRENCY_CODE_PATTERN)]],
    captionI18n: [emptyI18nText() as I18nText],
    minorUnits: [2, [Validators.required, Validators.min(0), Validators.max(4), integerValidator]],
  });

  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  constructor() {
    effect(() => {
      const currency = this.initial();
      this.form.reset(
        {
          code: currency?.code ?? '',
          captionI18n: (currency?.captionI18n as I18nText | undefined) ?? emptyI18nText(),
          minorUnits: currency?.minorUnits ?? 2,
        },
        { emitEvent: false },
      );
      this.invalidCount.set(0);
    });

    // Edit: code and decimals are fixed. Read-only: everything is.
    effect(() => {
      const isEdit = this.mode() === 'edit';
      const readOnly = this.readOnly();
      const { code, minorUnits, captionI18n } = this.form.controls;
      for (const control of [code, minorUnits]) {
        if (isEdit || readOnly) {
          control.disable({ emitEvent: false });
        } else {
          control.enable({ emitEvent: false });
        }
      }
      if (readOnly) {
        captionI18n.disable({ emitEvent: false });
      } else {
        captionI18n.enable({ emitEvent: false });
      }
    });

    effect(() => {
      const errors = this.fieldErrors();
      markServerErrors(this.form.controls, errors);
      // A server error on a field (e.g. the code already exists): move the focus there.
      const first = FIELD_ORDER.find((name) => (errors[name]?.length ?? 0) > 0);
      if (first) {
        afterNextRender(() => this.focusField(first), { injector: this.injector });
      }
    });
  }

  /** The code is always typed in capitals (ISO 4217). */
  protected onCodeInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const upper = input.value.toUpperCase();
    if (upper !== input.value) {
      input.value = upper;
      this.form.controls.code.setValue(upper);
    }
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
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
      code: raw.code.trim().toUpperCase(),
      captionI18n: raw.captionI18n,
      minorUnits: Number(raw.minorUnits),
    });
  }

  private focusField(name: string | undefined): void {
    if (!name) {
      return;
    }
    const selector =
      name === 'captionI18n' ? '[formcontrolname="captionI18n"] input[data-lang="de"]' : `[formcontrolname="${name}"]`;
    this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
  }
}
