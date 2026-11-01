import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Permission } from '../../../api/platform/models/permission';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { I18nText, emptyI18nText } from '../../../shared/ui/i18n-text.model';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { RolesFacade } from './roles.facade';

/** One row of the permission matrix: a `resourceType` and its `Permission` per `action`. */
export interface PermissionMatrixRow {
  readonly resourceType: string;
  readonly cells: ReadonlyMap<string, Permission>;
}

/**
 * `Role` detail/create page (`access.md §4`, `conventions-frontend.md
 * §4`). No `id` route param (mounted at `roles/new`) means create mode; an
 * `id` param means edit mode and triggers `facade.load(id)`.
 *
 * Two tabs (`mat-tab-group`):
 * - "General": `code`/`captionI18n`/`description`. `code` is disabled in
 *   edit mode — `UpdateRoleRequest` has no `code` field at all, only
 *   `CreateRoleRequest` does, so there is nothing to send for it after
 *   creation. `captionI18n`/`description` stay editable even for
 *   `isSystem` roles; only `code` (and the server-enforced `isSystem`
 *   itself) are immutable, not the whole row.
 * - "Historial de permisos": the full permission catalog
 *   (`facade.listAllPermissions()`) rendered as a `resourceType` x
 *   `action` matrix. With no `asOf` ("now"), checkboxes are editable and a
 *   "Guardar" button sends the complete desired `permissionCodes` list
 *   (`ACC-R7`: full-replace, the server computes the diff). Picking a past
 *   `asOf` date re-fetches `getRolePermissions` for that instant and makes
 *   the matrix read-only — this is what "selector de fecha (asOf)" means
 *   in the ficha.
 *
 * Disabled/hidden before the role exists (create mode): the permissions
 * tab makes no sense before `POST /v1/roles` has returned an id.
 *
 * Error handling follows the same hand-off as `LegalEntityDetailPage` with
 * `errorInterceptor`: 401/403/404 navigation and the 409-concurrency
 * dialog are already handled there.
 */
@Component({
  selector: 'nx-role-detail-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslocoModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    NxCanDirective,
    NxStatusBadgeComponent,
    NxI18nCaptionEditorComponent,
  ],
  templateUrl: './role-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleDetailPage implements OnInit {
  protected readonly facade = inject(RolesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly permissionsStore = inject(PermissionsStore);
  private readonly fb = inject(FormBuilder);

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly entity = this.facade.selected;
  protected readonly loading = this.facade.loading;

  protected readonly form = this.fb.group({
    code: this.fb.control('', { nonNullable: true, validators: [Validators.required] }),
    captionI18n: this.fb.control<I18nText>(emptyI18nText(), { nonNullable: true }),
    description: this.fb.control('', { nonNullable: true }),
  });

  // --- Permission matrix (tab 2) state ---
  protected readonly allPermissions = signal<Permission[]>([]);
  protected readonly loadingPermissions = signal(false);
  protected readonly savingPermissions = signal(false);
  protected readonly permissionsError = signal<string | null>(null);
  /** `''` means "now" (editable); any other value is a past `asOf` instant (read-only). */
  protected readonly asOf = signal<string>('');
  protected readonly grantedCodes = signal<ReadonlySet<string>>(new Set());

  protected readonly readOnlyMatrix = computed(() => this.asOf() !== '');
  protected readonly canEditPermissions = computed(
    () => !this.readOnlyMatrix() && this.permissionsStore.has('ROLE:UPDATE'),
  );

  protected readonly matrixColumns = computed<string[]>(() => {
    const actions = new Set<string>();
    for (const permission of this.allPermissions()) {
      actions.add(permission.action);
    }
    return [...actions].sort();
  });

  protected readonly matrixRows = computed<PermissionMatrixRow[]>(() => {
    const byResource = new Map<string, Map<string, Permission>>();
    for (const permission of this.allPermissions()) {
      const row = byResource.get(permission.resourceType) ?? new Map<string, Permission>();
      row.set(permission.action, permission);
      byResource.set(permission.resourceType, row);
    }
    return [...byResource.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([resourceType, cells]) => ({ resourceType, cells }));
  });

  constructor() {
    effect(() => {
      const current = this.entity();
      this.form.patchValue(
        {
          code: current?.code ?? '',
          captionI18n: current?.captionI18n ?? emptyI18nText(),
          description: current?.description ?? '',
        },
        { emitEvent: false },
      );
    });

    effect(() => {
      // `code` is immutable after creation — `UpdateRoleRequest` has no `code` field.
      if (this.mode() === 'edit') {
        this.form.controls.code.disable({ emitEvent: false });
      } else {
        this.form.controls.code.enable({ emitEvent: false });
      }
    });
  }

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.mode.set('edit');
      void this.initEdit(id);
    } else {
      this.mode.set('create');
    }
  }

  private async initEdit(id: string): Promise<void> {
    await this.facade.load(id);
    await Promise.all([this.loadPermissionCatalog(), this.loadPermissionHistory()]);
  }

  private async loadPermissionCatalog(): Promise<void> {
    try {
      const permissions = await this.facade.listAllPermissions();
      this.allPermissions.set(permissions);
    } catch {
      this.permissionsError.set(this.transloco.translate('errors.generic'));
    }
  }

  protected async onAsOfChange(value: string): Promise<void> {
    // Picking *today's* date would otherwise convert to a midnight-UTC timestamp (`loadPermissionHistory`),
    // which can be earlier than "now" and would hide same-day permission changes while also making the
    // matrix read-only — not what a user picking "today" expects. Treat it exactly like no date picked
    // ("now", editable) instead. Compared as date-only strings to sidestep timezone edge cases that a
    // full-timestamp comparison would run into.
    this.asOf.set(this.isToday(value) ? '' : value);
    await this.loadPermissionHistory();
  }

  /** `value` is the date-only (`YYYY-MM-DD`) string from an `<input type="date">`. */
  private isToday(value: string): boolean {
    const now = new Date();
    const todayOnly = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    return value === todayOnly;
  }

  private async loadPermissionHistory(): Promise<void> {
    const entity = this.entity();
    if (!entity) {
      return;
    }
    this.loadingPermissions.set(true);
    this.permissionsError.set(null);
    try {
      const asOfParam = this.asOf() ? new Date(this.asOf()).toISOString() : undefined;
      const grants = await this.facade.getPermissions(entity.id, asOfParam);
      this.grantedCodes.set(new Set(grants.map((grant) => grant.code)));
    } catch {
      this.permissionsError.set(this.transloco.translate('errors.generic'));
    } finally {
      this.loadingPermissions.set(false);
    }
  }

  protected isGranted(permission: Permission | undefined): boolean {
    return !!permission && this.grantedCodes().has(permission.code);
  }

  protected toggleGrant(permission: Permission): void {
    if (!this.canEditPermissions()) {
      return;
    }
    const next = new Set(this.grantedCodes());
    if (next.has(permission.code)) {
      next.delete(permission.code);
    } else {
      next.add(permission.code);
    }
    this.grantedCodes.set(next);
  }

  protected async onSavePermissions(): Promise<void> {
    const entity = this.entity();
    if (!entity || !this.canEditPermissions()) {
      return;
    }
    this.savingPermissions.set(true);
    this.permissionsError.set(null);
    try {
      const grants = await this.facade.savePermissions(entity.id, [...this.grantedCodes()]);
      this.grantedCodes.set(new Set(grants.map((grant) => grant.code)));
    } catch (error: unknown) {
      this.permissionsError.set(this.extractErrorMessage(error));
    } finally {
      this.savingPermissions.set(false);
    }
  }

  protected async onSave(): Promise<void> {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});
    const raw = this.form.getRawValue();

    try {
      if (this.mode() === 'create') {
        const created = await this.facade.create({
          code: raw.code,
          captionI18n: raw.captionI18n,
          description: raw.description || null,
        });
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          await this.facade.update(id, {
            captionI18n: raw.captionI18n,
            description: raw.description || null,
          });
        }
      }
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  protected async onDeactivate(): Promise<void> {
    const entity = this.entity();
    if (!entity) {
      return;
    }
    const confirmed = globalThis.confirm(this.transloco.translate('access.roles.confirmDeactivate'));
    if (!confirmed) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.facade.deactivate(entity.id);
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  private handleSaveError(error: unknown): void {
    const httpError = error as { status?: number; error?: unknown } | undefined;
    const body = httpError?.error;

    if (httpError?.status === 400 && isProblem(body) && body.errors) {
      this.fieldErrors.set(body.errors);
      this.saveError.set(this.transloco.translate('errors.validation'));
      return;
    }

    if (httpError?.status === 422 && isProblem(body)) {
      this.saveError.set(this.i18n.translateErrorCode(body.code));
      return;
    }

    if (httpError?.status === 409) {
      // `errorInterceptor` already opened the concurrency dialog; nothing else to show here.
      return;
    }

    this.saveError.set(this.transloco.translate('errors.generic'));
  }

  private extractErrorMessage(error: unknown): string {
    const httpError = error as { status?: number; error?: unknown } | undefined;
    const body = httpError?.error;
    if (httpError?.status === 422 && isProblem(body)) {
      return this.i18n.translateErrorCode(body.code);
    }
    return this.transloco.translate('errors.generic');
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }
}
