import { ChangeDetectionStrategy, Component, EventEmitter, Output, effect, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { I18nText, emptyI18nText } from '../../../shared/ui/i18n-text.model';

/** Form output, mode-agnostic; the detail page builds the right request shape for create/update. */
export interface BusinessUnitFormValue {
  readonly legalName: string;
  readonly displayName: string;
  readonly legalEntityId: string;
  readonly code: string;
  readonly captionI18n: I18nText;
  readonly costCenterCode: string;
}

/**
 * Reactive form for `BusinessUnit` (`conventions-frontend.md §4`).
 * `legalName`/`displayName`/`legalEntityId` only apply on create —
 * `UpdateBusinessUnitRequest` has no such fields (the `Party` and the
 * `LegalEntity` link are immutable after creation) — so in edit mode this
 * swaps the `nx-entity-picker` for a read-only display of the linked legal
 * entity's `code` (`legalEntityDisplay`, supplied by the detail page, which
 * already has to fetch that legal entity to show it).
 */
@Component({
  selector: 'nx-business-unit-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    TranslocoModule,
    NxEntityPickerComponent,
    NxI18nCaptionEditorComponent,
  ],
  templateUrl: './business-unit-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<BusinessUnit | null>(null);
  readonly saving = input<boolean>(false);
  /** Read-only display of the linked legal entity's `code`, shown instead of the picker in edit mode. */
  readonly legalEntityDisplay = input<string>('');
  readonly legalEntitySearch = input.required<(query: string) => Observable<LegalEntityListItem[]>>();
  /** Field name (as returned by the `Problem.errors` 400 body) → translated/translatable messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  @Output() readonly save = new EventEmitter<BusinessUnitFormValue>();

  protected readonly form;
  protected readonly legalEntityDisplayWith = (item: LegalEntityListItem): string => item.code;

  constructor(fb: FormBuilder) {
    this.form = fb.group({
      legalName: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      displayName: fb.control('', { nonNullable: true }),
      legalEntityId: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      code: fb.control('', { nonNullable: true, validators: [Validators.required] }),
      captionI18n: fb.control<I18nText>(emptyI18nText(), { nonNullable: true }),
      costCenterCode: fb.control('', { nonNullable: true }),
    });

    effect(() => {
      const entity = this.initial();
      this.form.patchValue(
        {
          legalName: '',
          displayName: '',
          legalEntityId: entity?.legalEntityId ?? '',
          code: entity?.code ?? '',
          captionI18n: entity?.captionI18n ?? emptyI18nText(),
          costCenterCode: entity?.costCenterCode ?? '',
        },
        { emitEvent: false },
      );
    });

    effect(() => {
      const isEdit = this.mode() === 'edit';
      for (const name of ['legalName', 'displayName', 'legalEntityId'] as const) {
        if (isEdit) {
          this.form.controls[name].disable({ emitEvent: false });
        } else {
          this.form.controls[name].enable({ emitEvent: false });
        }
      }
    });
  }

  protected onLegalEntitySelected(entity: LegalEntityListItem): void {
    this.form.controls.legalEntityId.setValue(entity.id);
    this.form.controls.legalEntityId.markAsDirty();
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
      legalEntityId: raw.legalEntityId,
      code: raw.code,
      captionI18n: raw.captionI18n,
      costCenterCode: raw.costCenterCode,
    });
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }
}
