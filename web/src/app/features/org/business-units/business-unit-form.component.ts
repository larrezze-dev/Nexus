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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule } from '@jsverse/transloco';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { Party } from '../../../api/platform/models/party';
import { I18nText, emptyI18nText } from '../../../shared/ui/i18n-text.model';
import {
  NxActionBarComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { BusinessUnitLegalEntityOption } from './business-units.facade';

/** Form output, mode-agnostic; the detail page builds the right request shape for create/update. */
export interface BusinessUnitFormValue {
  readonly legalName: string;
  readonly displayName: string;
  readonly legalEntityId: string;
  readonly code: string;
  readonly captionI18n: I18nText;
  readonly costCenterCode: string;
}

/** Controls in the order they appear on screen (the first invalid one gets the focus). */
const FIELD_ORDER = ['code', 'legalEntityId', 'costCenterCode', 'captionI18n', 'legalName', 'displayName'] as const;

/** Fixed once the unit exists (not in `UpdateBusinessUnitRequest`). */
const CREATE_ONLY: ReadonlySet<string> = new Set(['legalName', 'displayName', 'legalEntityId']);

/**
 * Business-unit form (`conventions-frontend.md §4.5`, form template), three sections on the
 * 12-column grid and the action bar (Cancel · Save changes / Create):
 * - «Identification»: code, legal entity (select showing «code + legal name») and cost center.
 * - «Name in each language»: the `captionI18n` editor (German required).
 * - «Party record»: legal name and short name of the unit's internal `Party` (ORG-R1).
 *
 * The legal entity and the party names are set on create only — `UpdateBusinessUnitRequest`
 * has no such fields — so in edit mode they are read-only with a lock and a hint. In edit mode
 * the party section is left out when the party could not be read (nothing to show).
 * On an invalid submit: an alert with the number of fields to check, focus on the first one.
 */
@Component({
  selector: 'nx-business-unit-form',
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
    NxI18nCaptionEditorComponent,
    NxSpanDirective,
  ],
  templateUrl: './business-unit-form.component.html',
  styles: `
    .nx-bu-form {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      min-width: 0;
    }

    .nx-bu-form__code {
      margin-right: var(--nx-space-1);
      color: var(--nx-color-text-muted);
    }

    .nx-bu-form__lock {
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<BusinessUnit | null>(null);
  /** Party of the unit being edited: legal name and short name (read-only in edit mode). */
  readonly party = input<Party | null>(null);
  /** Legal entities (active and inactive): create offers the active ones, edit shows the current one. */
  readonly legalEntities = input<readonly BusinessUnitLegalEntityOption[]>([]);
  readonly saving = input<boolean>(false);
  /** No permission to change it: every field read-only and Save disabled. */
  readonly readOnly = input<boolean>(false);
  /** Field name (as returned by the `Problem.errors` 400 body) → translated/translatable messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  readonly save = output<BusinessUnitFormValue>();
  readonly cancel = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', Validators.required],
    legalEntityId: ['', Validators.required],
    costCenterCode: [''],
    captionI18n: [emptyI18nText() as I18nText],
    legalName: ['', Validators.required],
    displayName: [''],
  });

  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  /** Legal entities to pick on create (active only); in edit mode, the current one. */
  protected readonly legalEntityChoices = computed<readonly BusinessUnitLegalEntityOption[]>(() => {
    const all = this.legalEntities();
    const current = this.initial()?.legalEntityId;
    return current ? all.filter((option) => option.id === current) : all.filter((option) => option.isActive);
  });

  /** In edit mode the party section is shown only if the party could be read. */
  protected readonly showPartySection = computed(() => this.mode() === 'create' || this.party() !== null);

  constructor() {
    effect(() => {
      this.initial();
      this.party();
      this.resetToInitial();
    });

    // Edit mode: legal entity and party names are fixed. Read-only mode: everything is.
    effect(() => {
      const isEdit = this.mode() === 'edit';
      const readOnly = this.readOnly();
      for (const name of FIELD_ORDER) {
        const control = this.form.controls[name];
        if (readOnly || (isEdit && CREATE_ONLY.has(name))) {
          control.disable({ emitEvent: false });
        } else {
          control.enable({ emitEvent: false });
        }
      }
    });
  }

  /** Back to the loaded values (or empty on create), pristine, without the error alert. */
  resetToInitial(): void {
    const unit = this.initial();
    const party = this.party();
    this.form.reset(
      {
        code: unit?.code ?? '',
        legalEntityId: unit?.legalEntityId ?? '',
        costCenterCode: unit?.costCenterCode ?? '',
        captionI18n: unit?.captionI18n ?? emptyI18nText(),
        legalName: party?.legalName ?? '',
        displayName: party?.displayName ?? '',
      },
      { emitEvent: false },
    );
    this.invalidCount.set(0);
  }

  protected legalEntityById(id: string): BusinessUnitLegalEntityOption | undefined {
    return this.legalEntities().find((option) => option.id === id);
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
      legalName: raw.legalName.trim(),
      displayName: raw.displayName.trim(),
      legalEntityId: raw.legalEntityId,
      code: raw.code.trim(),
      captionI18n: raw.captionI18n,
      costCenterCode: raw.costCenterCode.trim(),
    });
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
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
