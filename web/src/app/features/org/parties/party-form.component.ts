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
import { TranslocoModule } from '@jsverse/transloco';
import type { Party } from '../../../api/platform/models/party';
import {
  NxActionBarComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { partyScopeKey, partyTypeKey } from './party-labels';

/** Form output, mode-agnostic; the detail page builds the right request shape for create/update. */
export interface PartyFormValue {
  readonly partyType: Party['partyType'];
  readonly isInternal: boolean;
  readonly legalName: string;
  readonly displayName: string;
  readonly taxId: string;
  readonly erpCompanyCode: string;
}

/** Controls in the order they appear on screen (the first invalid one gets the focus). */
const FIELD_ORDER = ['partyType', 'isInternal', 'legalName', 'displayName', 'taxId', 'erpCompanyCode'] as const;

/** Fixed once the party exists: not in `UpdatePartyRequest` (ORG-R2 for the type). */
const CREATE_ONLY: ReadonlySet<string> = new Set(['partyType', 'isInternal']);

/**
 * Party form (`conventions-frontend.md §4.5`, form template): «Identification» (type,
 * internal/external, legal name, short name) and «Tax and ERP» sections on the 12-column grid,
 * and the action bar (Cancel · Save changes / Create party).
 *
 * The type and the internal/external scope are chosen on create only — `UpdatePartyRequest` has
 * neither (ORG-R2) — so in edit mode they are shown read-only with a lock and a hint. A new
 * party defaults to an external organization. The name label follows the type: «Legal name»
 * for an organization, «Full name» for a person. On an invalid submit: an alert with the number
 * of fields to check and focus on the first one.
 */
@Component({
  selector: 'nx-party-form',
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
  templateUrl: './party-form.component.html',
  styles: `
    .nx-party-form {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      min-width: 0;
    }

    .nx-party-form__lock {
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartyFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<Party | null>(null);
  readonly saving = input<boolean>(false);
  /** No permission to change it: every field read-only and Save disabled. */
  readonly readOnly = input<boolean>(false);
  /** Field name (as returned by the `Problem.errors` 400 body) → messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  readonly save = output<PartyFormValue>();
  readonly cancel = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    partyType: ['ORGANIZATION' as Party['partyType'], Validators.required],
    isInternal: [false],
    legalName: ['', Validators.required],
    displayName: [''],
    taxId: [''],
    erpCompanyCode: [''],
  });

  protected readonly typeOptions: readonly Party['partyType'][] = ['ORGANIZATION', 'PERSON'];
  protected readonly scopeOptions: readonly boolean[] = [false, true];
  protected readonly typeKey = partyTypeKey;
  protected readonly scopeKey = partyScopeKey;

  private readonly partyTypeValue = toSignal(this.form.controls.partyType.valueChanges, {
    initialValue: this.form.controls.partyType.value,
  });

  /** The name is a person's full name or an organization's legal name. */
  protected readonly isPerson = computed(() => this.partyTypeValue() === 'PERSON');

  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  constructor() {
    effect(() => {
      this.initial();
      this.resetToInitial();
    });

    // Edit mode: type and scope are fixed. Read-only mode: everything is.
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

  /** Back to the loaded values (or the create defaults), pristine, without the error alert. */
  resetToInitial(): void {
    const party = this.initial();
    // `emitEvent` stays on so the name label follows the loaded type.
    this.form.reset({
      partyType: party?.partyType ?? 'ORGANIZATION',
      isInternal: party?.isInternal ?? false,
      legalName: party?.legalName ?? '',
      displayName: party?.displayName ?? '',
      taxId: party?.taxId ?? '',
      erpCompanyCode: party?.erpCompanyCode ?? '',
    });
    this.invalidCount.set(0);
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
      partyType: raw.partyType,
      isInternal: raw.isInternal,
      legalName: raw.legalName.trim(),
      displayName: raw.displayName.trim(),
      taxId: raw.taxId.trim(),
      erpCompanyCode: raw.erpCompanyCode.trim(),
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
