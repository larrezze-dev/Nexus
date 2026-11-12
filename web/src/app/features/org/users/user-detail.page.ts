import { Component, OnInit, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { LANGUAGE_OPTIONS } from '../../../core/i18n/supported-languages';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import type { CreateRoleAssignmentRequest } from '../../../api/platform/models/create-role-assignment-request';
import type { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import type { RoleAssignmentListItem } from '../../../api/platform/models/role-assignment-list-item';
import type { RoleListItem } from '../../../api/platform/models/role-list-item';
import type { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { RoleAssignmentsFacade } from '../../access/role-assignments/role-assignments.facade';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { SYSTEM_USER_ID, UsersFacade } from './users.facade';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';

interface UserForm {
  oid: FormControl<string>;
  fullName: FormControl<string>;
  email: FormControl<string>;
  preferredLanguage: FormControl<'de' | 'en' | 'fr' | 'it' | 'es'>;
}

/** `scopeType`s this page offers in the grant form (`ACC-R1`, `domain/access.md`).
 * `GROUP` is omitted: no Groups facade/list endpoint exists yet in `features/org/*` to back a
 * picker for it. `PROJECT` is omitted: projects don't exist as a concept before step 2.1
 * (ficha's "Fuera de alcance"). */
type GrantableScopeType = 'LEGAL_ENTITY' | 'BUSINESS_UNIT' | 'WAREHOUSE' | 'GLOBAL';
const GRANTABLE_SCOPE_TYPES: readonly GrantableScopeType[] = [
  'LEGAL_ENTITY',
  'BUSINESS_UNIT',
  'WAREHOUSE',
  'GLOBAL',
];

interface GrantForm {
  scopeType: FormControl<GrantableScopeType>;
  validFrom: FormControl<string>;
  validTo: FormControl<string>;
}

/**
 * User detail/create page (`*-detail.page`, `conventions-frontend.md §4`). A "General" tab
 * (the only one `User` itself needs: no `caption_i18n`, versions, documents or approvals) plus
 * an "Asignaciones" tab for `RoleAssignment` (`ACC-R1`, `domain/access.md`) once the user
 * exists — a brand-new, not-yet-saved user has no id to list/grant assignments against, so
 * that tab is hidden while `isNew()`.
 *
 * ORG-R6 (SYSTEM never edited/deactivated): this page refuses to load `SYSTEM_USER_ID` for
 * editing and hides the deactivate action for it; the server rejects it too regardless.
 */
@Component({
  selector: 'nx-user-detail-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatTabsModule,
    NxCanDirective,
    NxAuditTrailComponent,
    NxEntityPickerComponent,
  ],
  templateUrl: './user-detail.page.html',
})
export class UserDetailPage implements OnInit {
  protected readonly facade = inject(UsersFacade);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  protected readonly assignmentsFacade = inject(RoleAssignmentsFacade);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly languageOptions = LANGUAGE_OPTIONS;
  protected readonly isNew = signal(true);
  protected readonly isSystemUser = signal(false);
  protected readonly businessUnitId = signal<string | null>(null);
  protected readonly businessUnitLabel = signal<string | null>(null);

  protected readonly form = new FormGroup<UserForm>({
    oid: new FormControl('', { nonNullable: true }),
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
    preferredLanguage: new FormControl('de', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly searchBusinessUnits = this.facade.searchBusinessUnits;
  protected readonly displayBusinessUnit = (bu: BusinessUnitListItem): string => this.facade.formatBusinessUnit(bu);

  // --- "Asignaciones" tab (ACC-R1, domain/access.md) ---------------------------------------

  protected readonly scopeTypes = GRANTABLE_SCOPE_TYPES;
  protected readonly assignments = signal<RoleAssignmentListItem[]>([]);

  protected readonly roleId = signal<string | null>(null);
  protected readonly roleLabel = signal<string | null>(null);
  protected readonly scopeEntityId = signal<string | null>(null);
  protected readonly scopeEntityLabel = signal<string | null>(null);

  /** Errors for the grant form specifically — kept separate from `facade.error()`/
   * `facade.fieldErrors()`, which belong to the "General" tab's `User` form. */
  protected readonly assignmentSaveError = signal<string | null>(null);
  protected readonly assignmentFieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly grantForm = new FormGroup<GrantForm>({
    scopeType: new FormControl<GrantableScopeType>('LEGAL_ENTITY', { nonNullable: true }),
    validFrom: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    validTo: new FormControl('', { nonNullable: true }),
  });

  protected readonly searchRoles = this.assignmentsFacade.searchRoles;
  protected readonly searchLegalEntities = this.assignmentsFacade.searchLegalEntities;
  protected readonly searchBusinessUnitsForScope = this.assignmentsFacade.searchBusinessUnits;
  protected readonly searchWarehouses = this.assignmentsFacade.searchWarehouses;

  protected readonly displayRole = (role: RoleListItem): string =>
    `${role.code} — ${resolveCaption(role.captionI18n, this.i18n.getActiveLanguage(), role.code)}`;
  protected readonly displayLegalEntity = (le: LegalEntityListItem): string => le.code;
  protected readonly displayBusinessUnitForScope = (bu: BusinessUnitListItem): string =>
    this.facade.formatBusinessUnit(bu);
  protected readonly displayWarehouse = (wh: WarehouseListItem): string =>
    `${wh.code} — ${resolveCaption(wh.captionI18n, this.i18n.getActiveLanguage(), wh.code)}`;

  /** Id from the route; the audit tab only shows for the record that matches it. */
  protected readonly routeId = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      this.isNew.set(true);
      this.facade.reset();
      return;
    }

    this.isNew.set(false);
    this.routeId.set(id);
    this.isSystemUser.set(id === SYSTEM_USER_ID);
    this.facade.reset();
    await this.facade.load(id);

    const user = this.facade.selected();
    this.breadcrumb.setCurrentLabel(user?.fullName ?? null);
    if (user) {
      this.form.setValue({
        oid: user.oid ?? '',
        fullName: user.fullName,
        email: user.email ?? '',
        preferredLanguage: user.preferredLanguage,
      });
      this.businessUnitId.set(user.businessUnitId ?? null);
      if (user.businessUnitId) {
        this.businessUnitLabel.set(await this.facade.describeBusinessUnit(user.businessUnitId));
      }
    }

    // ORG-R6: the SYSTEM user is never edited. Disable the whole form defensively on top of
    // hiding the save/deactivate actions in the template (the server rejects it either way).
    if (this.isSystemUser()) {
      this.form.disable();
    }

    // "Asignaciones" tab only exists once the user is saved (there is nothing to list/grant
    // against for a brand-new, not-yet-created user).
    // Skipped when the user failed to load (nothing to list assignments for).
    if (this.facade.selected()?.id === id) {
      await this.loadAssignments(id);
    }
  }

  private async loadAssignments(userId: string): Promise<void> {
    this.assignments.set(await this.assignmentsFacade.list(userId));
  }

  protected onBusinessUnitSelected(bu: BusinessUnitListItem): void {
    this.businessUnitId.set(bu.id);
    this.businessUnitLabel.set(this.facade.formatBusinessUnit(bu));
  }

  protected clearBusinessUnit(): void {
    this.businessUnitId.set(null);
    this.businessUnitLabel.set(null);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const businessUnitId = this.businessUnitId();

    if (this.isNew()) {
      const created = await this.facade.create({
        oid: value.oid,
        fullName: value.fullName,
        email: value.email || null,
        preferredLanguage: value.preferredLanguage,
        businessUnitId,
      });
      if (created) {
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      }
      return;
    }

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    await this.facade.update(id, {
      oid: value.oid || null,
      fullName: value.fullName,
      email: value.email || null,
      preferredLanguage: value.preferredLanguage,
      businessUnitId,
    });
  }

  protected async deactivate(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id || this.isSystemUser()) {
      return;
    }
    await this.facade.deactivate(id);
  }

  // --- "Asignaciones" tab ------------------------------------------------------------------

  protected onRoleSelected(role: RoleListItem): void {
    this.roleId.set(role.id);
    this.roleLabel.set(this.displayRole(role));
  }

  protected clearRole(): void {
    this.roleId.set(null);
    this.roleLabel.set(null);
  }

  protected onLegalEntityScopeSelected(entity: LegalEntityListItem): void {
    this.scopeEntityId.set(entity.id);
    this.scopeEntityLabel.set(this.displayLegalEntity(entity));
  }

  protected onBusinessUnitScopeSelected(entity: BusinessUnitListItem): void {
    this.scopeEntityId.set(entity.id);
    this.scopeEntityLabel.set(this.displayBusinessUnitForScope(entity));
  }

  protected onWarehouseScopeSelected(entity: WarehouseListItem): void {
    this.scopeEntityId.set(entity.id);
    this.scopeEntityLabel.set(this.displayWarehouse(entity));
  }

  protected clearScopeEntity(): void {
    this.scopeEntityId.set(null);
    this.scopeEntityLabel.set(null);
  }

  /** Resets the scope entity whenever `scopeType` changes: a picked `LegalEntity` id is
   * meaningless once the user switches to `BUSINESS_UNIT`, for instance. `GLOBAL` has no
   * entity at all (`scopeEntityId: null`, ACC-R1). */
  protected onScopeTypeChanged(): void {
    this.clearScopeEntity();
  }

  /** Whether the grant form has everything required to submit: a role, and — unless
   * `scopeType` is `GLOBAL` (`ACC-R1`: no entity at all) — a scope entity too. Drives
   * `assignment-grant-button`'s `disabled` state so a missing pick disables the button
   * instead of a silent no-op on submit. */
  protected canSubmitAssignment(): boolean {
    if (!this.roleId()) {
      return false;
    }
    const scopeType = this.grantForm.controls.scopeType.value;
    return scopeType === 'GLOBAL' || !!this.scopeEntityId();
  }

  protected async grantAssignment(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id || this.grantForm.invalid || !this.canSubmitAssignment()) {
      this.grantForm.markAllAsTouched();
      return;
    }
    const roleId = this.roleId();
    if (!roleId) {
      return;
    }
    const value = this.grantForm.getRawValue();
    const scopeType = value.scopeType;

    const request: CreateRoleAssignmentRequest = {
      roleId,
      userId: id,
      scopeType,
      scopeEntityId: scopeType === 'GLOBAL' ? null : this.scopeEntityId(),
      validFrom: `${value.validFrom}T00:00:00Z`,
      validTo: value.validTo ? `${value.validTo}T00:00:00Z` : null,
    };

    this.assignmentSaveError.set(null);
    this.assignmentFieldErrors.set({});
    try {
      await this.assignmentsFacade.create(request);
      await this.loadAssignments(id);

      this.clearRole();
      this.clearScopeEntity();
      this.grantForm.reset({ scopeType: 'LEGAL_ENTITY', validFrom: '', validTo: '' });
    } catch (error: unknown) {
      this.handleAssignmentError(error);
    }
  }

  /** Only shown in the template for rows where `assignmentsFacade.canRevoke(id)` is true
   * (contract gap: see `RoleAssignmentsFacade`'s doc comment). */
  protected async revokeAssignment(assignmentId: string): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    this.assignmentSaveError.set(null);
    this.assignmentFieldErrors.set({});
    try {
      await this.assignmentsFacade.revoke(assignmentId);
      await this.loadAssignments(id);
    } catch (error: unknown) {
      this.handleAssignmentError(error);
    }
  }

  /** Same 400/422/409 hand-off with `errorInterceptor` as `LegalEntityDetailPage`'s
   * `handleSaveError`: 400 -> per-field messages, 422 -> translated business-rule message
   * (e.g. `ACC-R1`/`ACC-R2`/`ACC-R5`), 409 -> `errorInterceptor`'s dialog already handled it. */
  private handleAssignmentError(error: unknown): void {
    const httpError = error as { status?: number; error?: unknown } | undefined;
    const body = httpError?.error;

    if (httpError?.status === 400 && isProblem(body) && body.errors) {
      this.assignmentFieldErrors.set(body.errors);
      this.assignmentSaveError.set(this.transloco.translate('errors.validation'));
      return;
    }

    if (httpError?.status === 422 && isProblem(body)) {
      this.assignmentSaveError.set(this.i18n.translateErrorCode(body.code));
      return;
    }

    if (httpError?.status === 409) {
      // `errorInterceptor` already opened the concurrency dialog; nothing else to show here.
      return;
    }

    this.assignmentSaveError.set(this.transloco.translate('errors.generic'));
  }

  protected assignmentErrorsFor(field: string): readonly string[] {
    return this.assignmentFieldErrors()[field] ?? [];
  }
}
