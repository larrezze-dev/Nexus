import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { startWith } from 'rxjs';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import type { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import type { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { markServerErrors } from '../../../shared/forms/server-errors';
import { NxConfirm } from '../../../shared/ui/layout';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import {
  GRANTABLE_SCOPE_TYPES,
  createRoleAssignmentForm,
  resetRoleAssignmentForm,
  toCreateRoleAssignmentRequest,
  type ScopeEntityOption,
} from '../../access/role-assignments/role-assignment-form';
import { RoleAssignmentsFacade, type AssignmentRow } from '../../access/role-assignments/role-assignments.facade';

/** Route of the «What does this user see?» screen for `userId` (see `accessRoleAssignmentsRoutes`). */
export function effectiveScopeLink(userId: string): string[] {
  return ['/access/users', userId, 'effective-scope'];
}

/**
 * «Assignments» tab of a user: current assignments (role, scope, validity by name), the grant form
 * and revoke with confirmation. Everything goes through `RoleAssignmentsFacade`.
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
    MatInputModule,
    MatSelectModule,
    NxCanDirective,
    NxEntityPickerComponent,
  ],
  templateUrl: './user-assignments.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserAssignmentsComponent {
  protected readonly facade = inject(RoleAssignmentsFacade);
  private readonly transloco = inject(TranslocoService);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);

  readonly userId = input.required<string>();

  protected readonly scopeTypes = GRANTABLE_SCOPE_TYPES;
  protected readonly form = createRoleAssignmentForm();
  protected readonly effectiveScopeLink = computed(() => effectiveScopeLink(this.userId()));

  /** Alert text of the last failed grant/revoke. */
  protected readonly saveError = signal<string | null>(null);
  /** Server messages per control (`role`, `scopeEntity`, `validFrom`, `validTo`). */
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly scopeType = toSignal(this.form.controls.scopeType.valueChanges.pipe(startWith(this.form.controls.scopeType.value)), {
    initialValue: this.form.controls.scopeType.value,
  });

  protected readonly searchRoles = this.facade.searchRoles;
  protected readonly searchLegalEntities = this.facade.searchLegalEntities;
  protected readonly searchBusinessUnits = this.facade.searchBusinessUnits;
  protected readonly searchWarehouses = this.facade.searchWarehouses;
  protected readonly displayRole = this.facade.displayRole;
  protected readonly displayLegalEntity = this.facade.displayLegalEntity;
  protected readonly displayBusinessUnit = this.facade.displayBusinessUnit;
  protected readonly displayWarehouse = this.facade.displayWarehouse;
  protected readonly displayScopeEntity = (option: ScopeEntityOption): string => option.label;

  constructor() {
    // Another user: forget the previous one's list (late responses are dropped by the facade).
    effect(() => {
      const id = this.userId();
      untracked(() => {
        this.facade.reset();
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

  protected onLegalEntitySelected(entity: LegalEntityListItem): void {
    this.setScopeEntity(entity.id, this.facade.displayLegalEntity(entity));
  }

  protected onBusinessUnitSelected(entity: BusinessUnitListItem): void {
    this.setScopeEntity(entity.id, this.facade.displayBusinessUnit(entity));
  }

  protected onWarehouseSelected(entity: WarehouseListItem): void {
    this.setScopeEntity(entity.id, this.facade.displayWarehouse(entity));
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
      return;
    }
    this.saveError.set(null);
    this.fieldErrors.set({});
    const userId = this.userId();
    try {
      await this.facade.create(toCreateRoleAssignmentRequest(userId, this.form.getRawValue()));
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
    this.saveError.set(null);
    this.fieldErrors.set({});
    try {
      await this.facade.revoke(row.id);
      this.notify('org.users.detail.assignments.revoked');
      await this.facade.list(this.userId());
    } catch (error: unknown) {
      this.showError(error);
    }
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
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
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
  }
}
