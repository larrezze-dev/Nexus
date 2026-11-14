import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LANGUAGE_OPTIONS, type SupportedLanguage } from '../../../core/i18n/supported-languages';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { markServerErrors } from '../../../shared/forms/server-errors';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import { NxConfirm } from '../../../shared/ui/layout';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { UserAssignmentsComponent } from './user-assignments.component';
import { SystemUserReadOnlyError, UsersFacade } from './users.facade';

/** A picked business unit: just what the label needs. */
type BusinessUnitOption = Pick<BusinessUnitListItem, 'id' | 'code' | 'captionI18n'>;

type UserForm = {
  oid: FormControl<string>;
  fullName: FormControl<string>;
  email: FormControl<string>;
  preferredLanguage: FormControl<SupportedLanguage>;
  businessUnit: FormControl<BusinessUnitOption | null>;
};

/**
 * User detail/create page. A «General» tab plus «Assignments» (own component, once the user
 * exists) and «Audit».
 *
 * ORG-R6: the SYSTEM user is read-only for real: the form is disabled, there is no save or
 * deactivate button, and the facade refuses the writes anyway. Errors: a 409 `CONCURRENCY`
 * is the interceptor's dialog (nothing here); a 409 `CONFLICT` marks the `oid` field; a 422
 * `ORG-R9` marks `email`; a 400 shows `errors.validation` and marks the named fields.
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
    UserAssignmentsComponent,
  ],
  templateUrl: './user-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDetailPage implements OnInit {
  protected readonly facade = inject(UsersFacade);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly transloco = inject(TranslocoService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly permissions = inject(PermissionsStore);
  private readonly destroyRef = inject(DestroyRef);
  private destroyed = false;

  protected readonly languageOptions = LANGUAGE_OPTIONS;
  protected readonly isNew = signal(true);
  /** Id from the route; the audit and assignments tabs only show for the record that matches it. */
  protected readonly routeId = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  /** Server messages per form control (400/409/422), shown under the field. */
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly form = new FormGroup<UserForm>({
    oid: new FormControl('', { nonNullable: true }),
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
    preferredLanguage: new FormControl<SupportedLanguage>('de', { nonNullable: true, validators: [Validators.required] }),
    businessUnit: new FormControl<BusinessUnitOption | null>(null),
  });

  protected readonly searchBusinessUnits = this.facade.searchBusinessUnits;
  protected readonly displayBusinessUnit = (bu: BusinessUnitOption): string => this.facade.formatBusinessUnit(bu);

  /** ORG-R6: the SYSTEM user is loaded (or addressed by the route). */
  protected readonly isSystemUser = computed(() => this.facade.isSystem());

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('USER:UPDATE');
  });

  /** Every field read-only and no write action: SYSTEM, or no permission to change. */
  protected readonly readOnly = computed(() => !this.isNew() && (this.isSystemUser() || !this.canUpdate()));

  protected readonly current = this.facade.selected;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
    effect(() => {
      if (this.readOnly()) {
        this.form.disable({ emitEvent: false });
      } else if (this.form.disabled) {
        this.form.enable({ emitEvent: false });
      }
    });
    // The breadcrumb names the user.
    effect(() => {
      const name = this.facade.title();
      if (!this.isNew() && name) {
        this.breadcrumb.setCurrentLabel(name);
      }
    });
    effect(() => {
      if (!this.isNew() && this.facade.error()) {
        this.breadcrumb.setLoadFailed();
      }
    });
  }

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    this.isNew.set(!id);
    this.form.controls.oid.setValidators(id ? [] : [Validators.required]);
    if (id) {
      this.routeId.set(id);
      void this.load();
    }
  }

  /** Loads the record (also the «Retry» of the error state) and fills the form. */
  protected async load(): Promise<void> {
    this.breadcrumb.clearLoadFailed();
    const id = this.routeId();
    if (!id) {
      return;
    }
    await this.facade.load(id);
    if (this.destroyed) {
      return;
    }
    this.patchForm();
  }

  protected clearBusinessUnit(): void {
    this.form.controls.businessUnit.setValue(null);
    this.form.controls.businessUnit.markAsDirty();
  }

  protected onBusinessUnitSelected(bu: BusinessUnitListItem): void {
    this.form.controls.businessUnit.setValue(bu);
    this.form.controls.businessUnit.markAsDirty();
  }

  protected async save(): Promise<void> {
    if (this.facade.saving() || this.readOnly()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saveError.set(null);
    this.fieldErrors.set({});
    const value = this.form.getRawValue();
    const businessUnitId = value.businessUnit?.id ?? null;
    try {
      if (this.isNew()) {
        const created = await this.facade.create({
          oid: value.oid,
          fullName: value.fullName,
          email: value.email || null,
          preferredLanguage: value.preferredLanguage,
          businessUnitId,
        });
        this.notify('org.users.created');
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
        return;
      }
      const id = this.routeId();
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
      this.notify('org.users.saved');
      this.patchForm();
    } catch (error: unknown) {
      this.showError(error);
    }
  }

  protected async deactivate(): Promise<void> {
    const user = this.facade.selected();
    if (!user || this.readOnly()) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.users.deactivateDialog.title'),
      message: this.transloco.translate('org.users.deactivateDialog.message', { name: user.fullName }),
      confirmLabel: this.transloco.translate('org.users.actions.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    this.saveError.set(null);
    this.fieldErrors.set({});
    try {
      await this.facade.deactivate(user.id);
      this.notify('org.users.deactivated');
    } catch (error: unknown) {
      this.showError(error);
    }
  }

  private patchForm(): void {
    const user = this.facade.selected();
    if (!user) {
      return;
    }
    this.form.reset(
      {
        oid: user.oid ?? '',
        fullName: user.fullName,
        email: user.email ?? '',
        preferredLanguage: user.preferredLanguage,
        businessUnit: this.facade.businessUnitRef(),
      },
      { emitEvent: false },
    );
  }

  private showError(error: unknown): void {
    if (error instanceof SystemUserReadOnlyError) {
      this.saveError.set(this.facade.describeError(error).message);
      return;
    }
    const info = this.facade.describeError(error);
    if (info.kind === 'concurrency') {
      // `errorInterceptor` already opened the concurrency dialog.
      return;
    }
    markServerErrors(this.form.controls, info.fields);
    this.fieldErrors.set(info.fields);
    this.saveError.set(info.message);
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
  }
}
