import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { AbstractControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import type { ErrorStateMatcher } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable, map, of, startWith } from 'rxjs';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import { provideNxDatepickerIntl } from '../../../core/i18n/nx-datepicker-intl';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { markServerErrors } from '../../../shared/forms/server-errors';
import {
  NxActionBarComponent,
  NxConfirm,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import {
  GRANTABLE_SCOPE_TYPES,
  createRoleAssignmentForm,
  resetRoleAssignmentForm,
  toCreateRoleAssignmentRequest,
  type GrantableScopeType,
  type ScopeEntityOption,
} from '../../access/role-assignments/role-assignment-form';
import { RoleAssignmentsFacade, type AssignmentRow } from '../../access/role-assignments/role-assignments.facade';

/** Route of the «What does this user see?» screen for `userId` (see `accessRoleAssignmentsRoutes`). */
export function effectiveScopeLink(userId: string): string[] {
  return ['/access/users', userId, 'effective-scope'];
}

/** Form controls in screen order, with the element that gets the focus when invalid. */
const FIELD_ORDER: readonly { readonly control: 'role' | 'scopeEntity' | 'validFrom' | 'validTo'; readonly testId: string }[] = [
  { control: 'role', testId: 'assignment-role-picker' },
  { control: 'scopeEntity', testId: 'assignment-scope-entity-picker' },
  { control: 'validFrom', testId: 'assignment-valid-from-input' },
  { control: 'validTo', testId: 'assignment-valid-to-input' },
];

/**
 * «Assignments» tab of a user: a section with the current assignments (a table of role, scope,
 * validity, origin and status by name; cards below 600 px) and the link «What does this user
 * see?», then the «New assignment» form (sections + action bar, like the General tab) and revoke
 * with confirmation. Everything goes through `RoleAssignmentsFacade`. `readOnly` (the SYSTEM
 * user, ORG-R6) hides the grant form; SEED rows never offer «Revoke» (`canRevoke`).
 */
@Component({
  selector: 'nx-user-assignments',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    NxActionBarComponent,
    NxCanDirective,
    NxEntityPickerComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxSpanDirective,
    NxStatusBadgeComponent,
    NgTemplateOutlet,
  ],
  templateUrl: './user-assignments.component.html',
  styleUrl: './user-assignments.component.scss',
  providers: [provideNxDatepickerIntl()],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAssignmentsComponent {
  protected readonly facade = inject(RoleAssignmentsFacade);
  private readonly transloco = inject(TranslocoService);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly saveAlert = viewChild<ElementRef<HTMLElement>>('saveAlert');
  private readonly revokeAlert = viewChild<ElementRef<HTMLElement>>('revokeAlert');

  readonly userId = input.required<string>();
  /** No grant form (the SYSTEM user is read-only, ORG-R6). */
  readonly readOnly = input(false);

  protected readonly scopeTypes = GRANTABLE_SCOPE_TYPES;
  protected readonly form = createRoleAssignmentForm();
  protected readonly effectiveScopeLink = computed(() => effectiveScopeLink(this.userId()));

  /** Alert text of the last failed grant. */
  protected readonly saveError = signal<string | null>(null);
  /** Alert text of the last failed revoke; lives outside the grant form (shown without `ROLE_ASSIGNMENT:CREATE`). */
  protected readonly revokeError = signal<string | null>(null);
  /** Server messages per control (`role`, `scopeEntity`, `validFrom`, `validTo`). */
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  /** Fields to review after a submit with errors; 0 = no alert. */
  protected readonly invalidCount = signal(0);

  /** «Until» shows its error as soon as it is wrong (also when «from» changes after it). */
  protected readonly validToErrorState: ErrorStateMatcher = {
    isErrorState: (control: AbstractControl | null) =>
      !!control && control.invalid && (control.touched || control.dirty || control.hasError('dateRange')),
  };

  protected readonly scopeType = toSignal(this.form.controls.scopeType.valueChanges.pipe(startWith(this.form.controls.scopeType.value)), {
    initialValue: this.form.controls.scopeType.value,
  });

  protected readonly searchRoles = this.facade.searchRoles;
  protected readonly displayRole = this.facade.displayRole;
  protected readonly displayScopeEntity = (option: ScopeEntityOption): string => option.label;

  /** Search of the scope-entity picker for the chosen scope type, as «code — name» options. */
  protected readonly scopeEntitySearch = computed<(query: string) => Observable<ScopeEntityOption[]>>(() =>
    this.scopeEntitySearchFor(this.scopeType()),
  );

  constructor() {
    // Another user: forget the previous one's list (late responses are dropped by the facade).
    effect(() => {
      const id = this.userId();
      untracked(() => {
        this.facade.reset();
        this.revokeError.set(null);
        void this.facade.list(id);
      });
    });
  }

  protected onRoleSelected(role: RoleListItem): void {
    this.form.controls.role.setValue(role);
    this.form.controls.role.markAsDirty();
  }

  protected clearRole(): void {
    this.form.controls.role.setValue(null);
  }

  protected onScopeEntitySelected(option: ScopeEntityOption): void {
    this.setScopeEntity(option.id, option.label);
  }

  protected clearScopeEntity(): void {
    this.form.controls.scopeEntity.setValue(null);
  }

  protected async grant(): Promise<void> {
    if (this.facade.saving()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      const invalid = FIELD_ORDER.filter((field) => this.isInvalid(this.form.controls[field.control]));
      this.invalidCount.set(invalid.length);
      this.focusTestId(invalid[0]?.testId);
      return;
    }
    this.invalidCount.set(0);
    this.saveError.set(null);
    this.fieldErrors.set({});
    const userId = this.userId();
    try {
      const request = toCreateRoleAssignmentRequest(userId, this.form.getRawValue());
      await this.facade.create(request);
      resetRoleAssignmentForm(this.form);
      this.notify('org.users.detail.assignments.granted');
      await this.facade.list(userId);
    } catch (error: unknown) {
      this.showError(error);
    }
  }

  protected async revoke(row: AssignmentRow): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.users.detail.assignments.revokeDialog.title'),
      message: this.transloco.translate('org.users.detail.assignments.revokeDialog.message', {
        role: row.roleLabel,
        scope: row.scopeLabel ?? this.transloco.translate('org.users.detail.assignments.scopeTypes.GLOBAL'),
      }),
      confirmLabel: this.transloco.translate('org.users.detail.assignments.revoke'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    this.revokeError.set(null);
    try {
      await this.facade.revoke(row.id);
      this.notify('org.users.detail.assignments.revoked');
      await this.facade.list(this.userId());
    } catch (error: unknown) {
      this.showRevokeError(error);
    }
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }

  /** First message under a picker: «required» once touched, else the server's. */
  protected pickerError(field: 'role' | 'scopeEntity'): string | null {
    const control = this.form.controls[field];
    if (control.enabled && control.touched && control.hasError('required')) {
      return this.transloco.translate('shared.form.required');
    }
    return this.errorsFor(field)[0] ?? null;
  }

  /** «Entidad legal · Nüssli AG»; «Global» alone. */
  protected scopeText(row: AssignmentRow): string {
    const type = this.transloco.translate(`org.users.detail.assignments.scopeTypes.${row.scopeType}`);
    return row.scopeLabel ? `${type} · ${row.scopeLabel}` : type;
  }

  protected retryList(): void {
    void this.facade.list(this.userId());
  }

  private scopeEntitySearchFor(type: GrantableScopeType): (query: string) => Observable<ScopeEntityOption[]> {
    switch (type) {
      case 'LEGAL_ENTITY':
        return (q) =>
          this.facade.searchLegalEntities(q).pipe(map((items) => items.map((e) => ({ id: e.id, label: this.facade.displayLegalEntity(e) }))));
      case 'BUSINESS_UNIT':
        return (q) =>
          this.facade.searchBusinessUnits(q).pipe(map((items) => items.map((e) => ({ id: e.id, label: this.facade.displayBusinessUnit(e) }))));
      case 'WAREHOUSE':
        return (q) =>
          this.facade.searchWarehouses(q).pipe(map((items) => items.map((e) => ({ id: e.id, label: this.facade.displayWarehouse(e) }))));
      default:
        return () => of<ScopeEntityOption[]>([]);
    }
  }

  private isInvalid(control: AbstractControl): boolean {
    return control.enabled && control.invalid;
  }

  /** Focuses the field (or the input inside a picker) with that `data-testid`. */
  private focusTestId(testId: string | undefined): void {
    if (!testId) {
      return;
    }
    setTimeout(() => {
      const element = this.host.nativeElement.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
      const target = element?.matches('input') ? element : element?.querySelector<HTMLElement>('input');
      target?.focus();
    });
  }

  private setScopeEntity(id: string, label: string): void {
    this.form.controls.scopeEntity.setValue({ id, label });
    this.form.controls.scopeEntity.markAsDirty();
  }

  private showError(error: unknown): void {
    const info = this.facade.describeError(error);
    if (info.kind === 'concurrency') {
      // `errorInterceptor` already opened the concurrency dialog.
      return;
    }
    markServerErrors(this.form.controls, info.fields);
    this.fieldErrors.set(info.fields);
    this.saveError.set(info.message);
    if (info.message) {
      setTimeout(() => this.saveAlert()?.nativeElement.focus());
    } else {
      this.focusTestId(FIELD_ORDER.find((field) => info.fields[field.control]?.length)?.testId);
    }
  }

  /** Own alert for a failed revoke; a 409/422 means the list is stale (already revoked, last admin…), so reload it. */
  private showRevokeError(error: unknown): void {
    const info = this.facade.describeError(error);
    const status = (error as { status?: number } | null)?.status;
    if (info.kind === 'concurrency' || status === 409 || status === 422) {
      void this.facade.list(this.userId());
    }
    if (info.kind === 'concurrency') {
      // `errorInterceptor` already opened the concurrency dialog.
      return;
    }
    this.revokeError.set(info.message ?? this.transloco.translate('errors.generic'));
    setTimeout(() => this.revokeAlert()?.nativeElement.focus());
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
  }
}
