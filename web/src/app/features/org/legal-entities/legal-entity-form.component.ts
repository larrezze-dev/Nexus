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
import { CurrencyListItem } from '../../../api/platform/models/currency-list-item';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { Party } from '../../../api/platform/models/party';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { countryName, countryOptions } from '../../../shared/ui/country/country-names';
import {
  NxActionBarComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { LegalEntityGroupOption } from './legal-entities.facade';

/** Form output, mode-agnostic; the detail page builds the right request shape for create/update. */
export interface LegalEntityFormValue {
  readonly legalName: string;
  readonly displayName: string;
  readonly groupId: string;
  readonly code: string;
  readonly currencyCode: string;
  readonly countryCode: string;
}

/** Controls in the order they appear on screen (the first invalid one gets the focus). */
const FIELD_ORDER = ['legalName', 'displayName', 'code', 'groupId', 'countryCode', 'currencyCode'] as const;

/** Fixed once the entity exists (not in `UpdateLegalEntityRequest`). */
const CREATE_ONLY: ReadonlySet<string> = new Set(['legalName', 'displayName', 'groupId']);

/**
 * Legal-entity form (`conventions-frontend.md §4.5`, form template): «Company» and «Country and
 * currency» sections on the 12-column grid, and the action bar (Cancel · Save changes / Create).
 *
 * `legalName`, `displayName` and the group are set on create only — `UpdateLegalEntityRequest`
 * has no such fields (the `Party` and the group link are fixed, ORG-R7) — so in edit mode they
 * are shown read-only with a lock and a hint, the names coming from the entity's `Party`.
 * Group, country and currency are selects showing «code + name» of the value inside the field.
 * On an invalid submit: an alert with the number of fields to check, focus on the first one.
 */
@Component({
  selector: 'nx-legal-entity-form',
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
  templateUrl: './legal-entity-form.component.html',
  styles: `
    .nx-le-form {
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
      min-width: 0;
    }

    .nx-le-form__code {
      margin-right: var(--nx-space-1);
      color: var(--nx-color-text-muted);
    }

    .nx-le-form__lock {
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityFormComponent {
  readonly mode = input.required<'create' | 'edit'>();
  readonly initial = input<LegalEntity | null>(null);
  /** Party of the entity being edited: legal name and display name (read-only in edit mode). */
  readonly party = input<Party | null>(null);
  readonly groups = input<readonly LegalEntityGroupOption[]>([]);
  readonly currencies = input<readonly CurrencyListItem[]>([]);
  readonly saving = input<boolean>(false);
  /** No permission to change it: every field read-only and Save disabled. */
  readonly readOnly = input<boolean>(false);
  /** Field name (as returned by the `Problem.errors` 400 body) → translated/translatable messages. */
  readonly fieldErrors = input<Readonly<Record<string, readonly string[]>>>({});

  readonly save = output<LegalEntityFormValue>();
  readonly cancel = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    legalName: ['', Validators.required],
    displayName: [''],
    groupId: ['', Validators.required],
    code: ['', Validators.required],
    countryCode: ['', Validators.required],
    currencyCode: ['', Validators.required],
  });

  private readonly groupIdValue = toSignal(this.form.controls.groupId.valueChanges, { initialValue: '' });

  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  protected readonly countries = computed(() => countryOptions(this.lang()));

  /** Currencies to offer: the active ones, plus the current one if it is no longer active. */
  protected readonly currencyChoices = computed<readonly CurrencyListItem[]>(() => {
    const list = this.currencies();
    const current = this.initial()?.currencyCode;
    return current && !list.some((currency) => currency.code === current)
      ? [{ code: current, captionI18n: { de: '' }, isActive: false, minorUnits: 2 }, ...list]
      : list;
  });

  /** The group of the entity (edit) or the one picked so far (create). */
  protected readonly selectedGroup = computed(() => {
    const id = this.initial()?.groupId ?? this.groupIdValue();
    return this.groups().find((group) => group.id === id) ?? null;
  });

  constructor() {
    effect(() => {
      this.initial();
      this.party();
      this.resetToInitial();
    });

    // Edit mode: party fields and group are fixed. Read-only mode: everything is.
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
    const entity = this.initial();
    const party = this.party();
    this.form.reset(
      {
        legalName: party?.legalName ?? '',
        displayName: party?.displayName ?? '',
        groupId: entity?.groupId ?? '',
        code: entity?.code ?? '',
        countryCode: entity?.countryCode ?? '',
        currencyCode: entity?.currencyCode ?? '',
      },
      { emitEvent: false },
    );
    this.invalidCount.set(0);
  }

  protected countryLabel(code: string): string {
    return countryName(code, this.lang());
  }

  protected currencyLabel(currency: CurrencyListItem | undefined): string {
    return currency ? resolveCaption(currency.captionI18n, this.lang(), '') : '';
  }

  protected currencyByCode(code: string): CurrencyListItem | undefined {
    return this.currencyChoices().find((currency) => currency.code === code);
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
      groupId: raw.groupId,
      code: raw.code.trim(),
      currencyCode: raw.currencyCode.toUpperCase(),
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
