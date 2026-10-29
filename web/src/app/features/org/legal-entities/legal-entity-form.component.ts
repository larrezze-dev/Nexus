import { ChangeDetectionStrategy, Component, EventEmitter, Output, effect, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule } from '@jsverse/transloco';
import { LegalEntity } from '../../../api/platform/models/legal-entity';

/** Form output, mode-agnostic; the detail page builds the right request shape for create/update. */
export interface LegalEntityFormValue {
  readonly legalName: string;
  readonly displayName: string;
  readonly groupId: string;
  readonly code: string;
  readonly currencyCode: string;
  readonly countryCode: string;
}

/**
 * Reactive form for `LegalEntity` (`conventions-frontend.md §4`). `legalName`/
 * `displayName`/`groupId` only apply on create — `UpdateLegalEntityRequest` in
 * the contract has no such fields (they are immutable once the `Party` and
 * the group link are set, ORG-R7), so this component disables and hides
 * those three controls whenever `mode() === 'edit'`.
 */
@Component({
  selector: 'nx-legal-entity-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    TranslocoModule,
  ],
  templateUrl: './legal-entity-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<LegalEntity | null>(null);
  readonly saving = input<boolean>(false);
  /** Field name (as returned by the `Problem.errors` 400 body) → translated/translatable messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  @Output() readonly save = new EventEmitter<LegalEntityFormValue>();

  protected readonly form;

  constructor(fb: FormBuilder) {
    this.form = fb.group({
      legalName: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      displayName: fb.control('', { nonNullable: true }),
      groupId: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      code: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      // Case-insensitive: `onSubmit()` uppercases both before emitting, to match the contract's
      // `^[A-Z]{3}$`/`^[A-Z]{2}$` patterns without forcing the user to type in caps.
      currencyCode: fb.control('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)],
      }),
      countryCode: fb.control('', {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(/^[A-Za-z]{2}$/)],
      }),
    });

    effect(() => {
      const entity = this.initial();
      this.form.patchValue(
        {
          legalName: '',
          displayName: '',
          groupId: entity?.groupId ?? '',
          code: entity?.code ?? '',
          currencyCode: entity?.currencyCode ?? '',
          countryCode: entity?.countryCode ?? '',
        },
        { emitEvent: false },
      );
    });

    effect(() => {
      const isEdit = this.mode() === 'edit';
      for (const name of ['legalName', 'displayName', 'groupId'] as const) {
        if (isEdit) {
          this.form.controls[name].disable({ emitEvent: false });
        } else {
          this.form.controls[name].enable({ emitEvent: false });
        }
      }
    });
  }

  protected onSubmit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    this.save.emit({
      legalName: raw.legalName,
      displayName: raw.displayName,
      groupId: raw.groupId,
      code: raw.code,
      currencyCode: raw.currencyCode.toUpperCase(),
      countryCode: raw.countryCode.toUpperCase(),
    });
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }
}
