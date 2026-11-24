import { ChangeDetectionStrategy, Component, ElementRef, OnInit, computed, effect, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerInput, MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Permission } from '../../../api/platform/models/permission';
import {
  isConcurrencyConflict,
  isDuplicateConflict,
  isProblem,
  localizedFieldErrors,
} from '../../../core/http/problem';
import { DISPLAY_TIME_ZONE, NxFormatService } from '../../../core/i18n/nx-format.service';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NX_NOW } from '../../../core/time/nx-clock';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { I18nText, emptyI18nText } from '../../../shared/ui/i18n-text.model';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import {
  NxActionBarComponent,
  NxConfirm,
  NxDetailSkeletonComponent,
  NxEmptyStateComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxPageComponent,
  NxPageHeaderComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { markServerErrors } from '../../../shared/forms/server-errors';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { RolesFacade } from './roles.facade';
import { endOfZonedDay, zonedYmd } from '../../../core/time/zoned-day';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import {
  PermissionMatrix,
  buildPermissionMatrix,
  diffPermissions,
  SYSTEM_ADMIN_LOCKED_PERMISSIONS,
  SYSTEM_ADMIN_ROLE_CODE,
  humanizeCode,
} from './permission-matrix';

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
 * - "Permisos": the full permission catalog
 *   (`facade.listAllPermissions()`) rendered as a `resourceType` x
 *   `action` matrix (table with sticky header and first column; below
 *   600 px it scrolls sideways inside its own box, never the page). With no `asOf` ("now"), checkboxes are editable and a
 *   "Guardar" button sends the complete desired `permissionCodes` list
 *   (`ACC-R7`: full-replace, the server computes the diff). Picking a past
 *   `asOf` date re-fetches `getRolePermissions` for that instant and makes
 *   the matrix read-only — this is what "selector de fecha (asOf)" means
 *   in the ficha. The date is a `mat-datepicker` (locale of the active
 *   language), never a native date input.
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
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslocoModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    NxActionBarComponent,
    NxAuditTrailComponent,
    NxCanDirective,
    NxDetailSkeletonComponent,
    NxEmptyStateComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxSpanDirective,
    NxStatusBadgeComponent,
    NxI18nCaptionEditorComponent,
  ],
  templateUrl: './role-detail.page.html',
  styleUrl: './role-detail.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleDetailPage implements OnInit {
  protected readonly facade = inject(RolesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly permissionsStore = inject(PermissionsStore);
  private readonly fb = inject(FormBuilder);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly format = inject(NxFormatService);
  private readonly now = inject(NX_NOW);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  /** Emits once the active language's file is loaded (and on every switch): texts built in TS follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());
  private permissionsSeq = 0;

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  protected readonly entity = this.facade.selected;
  protected readonly loading = this.facade.loading;
  protected readonly loadError = this.facade.error;

  /** Role name in the active language (caption, the code if it has none). */
  protected readonly name = computed(() => {
    this.i18n.activeLang();
    const current = this.entity();
    return current ? resolveCaption(current.captionI18n, this.i18n.getActiveLanguage(), current.code) : '';
  });

  protected readonly canUpdate = computed(() => {
    this.permissionsStore.permissions();
    return this.permissionsStore.has('ROLE:UPDATE');
  });

  /** An existing role the user may not change: every field read-only (`.nx-readonly`), Save disabled. */
  protected readonly readOnly = computed(() => this.mode() === 'edit' && !this.canUpdate());

  /** «SYSTEM_ADMIN · modified on 11 Nov 2026», in the active language. */
  protected readonly subtitle = computed(() => {
    const current = this.entity();
    if (!current) {
      return '';
    }
    this.translation();
    const date = this.format.formatDateMedium(current.updatedAt);
    const modified = date ? this.transloco.translate('access.roles.detail.modified', { date }) : '';
    return [current.code, modified].filter(Boolean).join(' · ');
  });

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
  /** Grants as loaded from the server (the baseline of the diff). */
  protected readonly originalCodes = signal<ReadonlySet<string>>(new Set());
  /** Grants as currently ticked in the matrix (the draft). */
  protected readonly grantedCodes = signal<ReadonlySet<string>>(new Set());

  protected readonly readOnlyMatrix = computed(() => this.asOf() !== '');

  /** Latest day the «as of» picker offers (there is no future state to look at). */
  protected readonly today = ((): Date => {
    const [year, month, day] = zonedYmd(this.now(), DISPLAY_TIME_ZONE).split('-').map(Number);
    return new Date(year, month - 1, day);
  })();
  /** The picked day as a `Date` for `mat-datepicker` (local midnight); `null` = today. */
  protected readonly asOfDate = computed<Date | null>(() => {
    const value = this.asOf();
    if (!value) {
      return null;
    }
    const [year, month, day] = value.split('-').map(Number);
    return new Date(year, month - 1, day);
  });
  /** The picked day in the active language («1 Jan 2026», «01.01.2026»…), for the history notice. */
  protected readonly asOfLabel = computed(() => {
    const value = this.asOf();
    return value ? this.format.formatDateMedium(new Date(value).toISOString()) : '';
  });

  /** Placeholder rows and columns while the catalog or a past state is loading. */
  protected readonly skeletonRows = [0, 1, 2, 3, 4, 5];
  protected readonly skeletonColumns = computed(() => {
    const count = this.matrix().columns.length || 3;
    return Array.from({ length: count }, (_, index) => index);
  });
  protected readonly showMatrixSkeleton = computed(
    () => this.loadingPermissions() || (this.matrix().rows.length === 0 && !this.permissionsError()),
  );
  protected readonly canEditPermissions = computed(() => !this.readOnlyMatrix() && this.canUpdate());

  /** What saving would change: codes to open and to close (`ACC-R7`). */
  protected readonly permissionDiff = computed(() => diffPermissions(this.originalCodes(), this.grantedCodes()));
  protected readonly permissionsDirty = computed(() => {
    const diff = this.permissionDiff();
    return diff.added.length > 0 || diff.removed.length > 0;
  });

  /** Rows (resources) x columns (actions) with readable labels in the active language. */
  protected readonly matrix = computed<PermissionMatrix>(() => {
    this.translation();
    this.i18n.activeLang();
    return buildPermissionMatrix(this.allPermissions(), {
      language: this.i18n.getActiveLanguage(),
      resource: (resourceType) => this.labelFor(`access.permissions.resources.${resourceType}`, resourceType),
      action: (action) => this.labelFor(`access.permissions.actions.${action}`, action),
    });
  });

  private readonly asOfInput = viewChild(MatDatepickerInput<Date>);
  private patchedKey: string | null = null;

  constructor() {
    effect(() => {
      const current = this.entity();
      // A reload that brings the same role data (e.g. the ETag refresh after saving permissions) must not wipe
      // unsaved edits in General; saving General itself changes the data, so it still resets the form.
      const key = JSON.stringify([current?.id, current?.code, current?.captionI18n, current?.description]);
      if (key === this.patchedKey && this.form.dirty) {
        return;
      }
      this.patchedKey = key;
      this.form.patchValue(
        {
          code: current?.code ?? '',
          captionI18n: current?.captionI18n ?? emptyI18nText(),
          description: current?.description ?? '',
        },
        { emitEvent: false },
      );
      this.form.markAsPristine();
      this.invalidCount.set(0);
    });

    // Server field errors (400, or a 409 duplicate code) also mark the controls, so `mat-error` shows.
    effect(() => markServerErrors(this.form.controls, this.fieldErrors()));

    effect(() => {
      // Breadcrumb/title: the role name, following a language switch; «Not available» if the load failed.
      if (this.mode() !== 'edit') {
        return;
      }
      if (this.loadError()) {
        this.breadcrumb.setLoadFailed();
      } else {
        this.breadcrumb.setCurrentLabel(this.name() || null);
      }
    });

    effect(() => {
      if (this.mode() === 'edit' && !this.canUpdate()) {
        this.form.controls.captionI18n.disable({ emitEvent: false });
        this.form.controls.description.disable({ emitEvent: false });
      } else {
        this.form.controls.captionI18n.enable({ emitEvent: false });
        this.form.controls.description.enable({ emitEvent: false });
      }
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

  /** Loads the role (also the «Retry» of the error state), then its permission catalog and grants. */
  protected async initEdit(id = this.route.snapshot.paramMap.get('id') ?? ''): Promise<void> {
    this.breadcrumb.clearLoadFailed();
    await this.facade.load(id);
    if (this.facade.error()) {
      return;
    }
    await Promise.all([this.loadPermissionCatalog(), this.loadPermissionHistory()]);
  }

  /** Translation of `key`, or a readable form of `code` when the key has no text yet. */
  private labelFor(key: string, code: string): string {
    const text = this.transloco.translate(key);
    return text && text !== key ? text : humanizeCode(code);
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

  /** `value` is the date-only (`YYYY-MM-DD`) string of the day picked in the «as of» date picker. */
  private isToday(value: string): boolean {
    return value === zonedYmd(this.now(), DISPLAY_TIME_ZONE);
  }

  private async loadPermissionHistory(): Promise<void> {
    const entity = this.entity();
    if (!entity) {
      return;
    }
    const seq = ++this.permissionsSeq;
    this.loadingPermissions.set(true);
    this.permissionsError.set(null);
    try {
      // End of the picked day (Zurich, the zone dates are displayed in): validity is half-open, so a change made
      // during that day is visible when the user picks it.
      const asOfParam = this.asOf() ? endOfZonedDay(this.asOf(), DISPLAY_TIME_ZONE).toISOString() : undefined;
      const grants = await this.facade.getPermissions(entity.id, asOfParam);
      if (seq !== this.permissionsSeq) {
        return; // a later asOf pick (or reload) supersedes this response
      }
      const codes = new Set(grants.map((grant) => grant.code));
      this.originalCodes.set(codes);
      this.grantedCodes.set(new Set(codes));
    } catch {
      if (seq === this.permissionsSeq) {
        this.permissionsError.set(this.transloco.translate('errors.generic'));
      }
    } finally {
      if (seq === this.permissionsSeq) {
        this.loadingPermissions.set(false);
      }
    }
  }

  /** `SYSTEM_ADMIN`'s five critical permissions cannot be unticked (`ACC-R11`). */
  protected isLockedPermission(code: string | undefined): boolean {
    return !!code && this.entity()?.code === SYSTEM_ADMIN_ROLE_CODE && SYSTEM_ADMIN_LOCKED_PERMISSIONS.has(code);
  }

  protected isGranted(code: string | undefined): boolean {
    return !!code && this.grantedCodes().has(code);
  }

  /** Discards the unsaved ticks. */
  protected onDiscardPermissions(): void {
    this.grantedCodes.set(new Set(this.originalCodes()));
  }

  protected toggleGrant(permission: Pick<Permission, 'code'>): void {
    if (!this.canEditPermissions() || this.savingPermissions() || this.isLockedPermission(permission.code)) {
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
    if (!entity || !this.canEditPermissions() || !this.permissionsDirty() || this.savingPermissions()) {
      return;
    }
    this.savingPermissions.set(true);
    this.permissionsError.set(null);
    try {
      // Full replace (`ACC-R7`): the complete desired list, the server computes the diff.
      const grants = await this.facade.savePermissions(entity.id, [...this.grantedCodes()].sort());
      const codes = new Set(grants.map((grant) => grant.code));
      this.originalCodes.set(codes);
      this.grantedCodes.set(new Set(codes));
      this.notify('access.roles.permissionsSaved');
    } catch (error: unknown) {
      this.permissionsError.set(this.permissionsErrorMessage(error));
    } finally {
      this.savingPermissions.set(false);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
  }

  protected async onSave(): Promise<void> {
    if (this.saving() || this.readOnly()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      const invalid = (['code', 'description', 'captionI18n'] as const).filter((name) => this.form.controls[name].invalid);
      this.invalidCount.set(invalid.length);
      this.focusField(invalid[0]);
      return;
    }
    this.invalidCount.set(0);

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
          this.notify('access.roles.saved');
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
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('access.roles.deactivateDialog.title'),
      message: this.transloco.translate('access.roles.deactivateDialog.message', {
        name: this.name(),
        code: entity.code,
      }),
      confirmLabel: this.transloco.translate('access.roles.actions.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.facade.deactivate(entity.id);
      this.notify('access.roles.deactivated');
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  private handleSaveError(error: unknown): void {
    const httpError = error as { status?: number; error?: unknown } | undefined;
    const body = httpError?.error;

    if (httpError?.status === 400) {
      this.fieldErrors.set(
        localizedFieldErrors(isProblem(body) ? body.errors : undefined, this.transloco.translate('shared.form.invalidValue')),
      );
      this.saveError.set(this.transloco.translate('errors.validation'));
      return;
    }

    if (httpError?.status === 422 && isProblem(body)) {
      this.saveError.set(this.i18n.translateErrorCode(body.code));
      return;
    }

    if (isConcurrencyConflict(error)) {
      // `errorInterceptor` already opened the concurrency dialog; nothing else to show here.
      return;
    }

    if (isDuplicateConflict(error)) {
      this.fieldErrors.set({ code: [this.transloco.translate('errors.CONFLICT')] });
      return;
    }

    this.saveError.set(this.transloco.translate('errors.generic'));
  }

  /** Message of a failed permission save: 400 -> generic validation, 422 -> the business rule, 409 CONCURRENCY -> none (dialog). */
  private permissionsErrorMessage(error: unknown): string | null {
    const httpError = error as { status?: number; error?: unknown } | undefined;
    const body = httpError?.error;
    if (httpError?.status === 400) {
      return this.transloco.translate('errors.validation');
    }
    if (httpError?.status === 422 && isProblem(body)) {
      return this.i18n.translateErrorCode(body.code);
    }
    if (isConcurrencyConflict(error)) {
      // `errorInterceptor` already opened the concurrency dialog; the draft stays as the user left it.
      return null;
    }
    return this.transloco.translate('errors.generic');
  }

  /** «Cancel»: back to the role list. */
  protected onCancel(): void {
    void this.router.navigate(['..'], { relativeTo: this.route });
  }

  /** Date picked in the «as of» field (`null` = cleared or «Back to today»), as `YYYY-MM-DD`. */
  /** Puts the typed date back to the applied one when the change was refused. */
  private resetAsOfInput(applied: Date | null): void {
    const input = this.asOfInput();
    if (input) {
      input.value = applied;
    }
  }

  protected async onAsOfPicked(date: Date | null): Promise<void> {
    const previous = this.asOfDate();
    const unchanged = (date?.getTime() ?? null) === (previous?.getTime() ?? null);
    // Nothing to look at after today (the picker's `max` does not stop typed dates).
    const future = date !== null && date.getTime() > this.now();
    if (future || unchanged) {
      this.resetAsOfInput(previous);
      return;
    }
    if (this.permissionsDirty()) {
      const confirmed = await this.confirm.confirm({
        title: this.transloco.translate('access.roles.permissionMatrix.discardDialog.title'),
        message: this.transloco.translate('access.roles.permissionMatrix.discardDialog.message'),
        confirmLabel: this.transloco.translate('access.roles.permissionMatrix.discardDialog.confirm'),
        destructive: true,
      });
      if (!confirmed) {
        this.resetAsOfInput(previous);
        return;
      }
    }
    const value = date
      ? `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
      : '';
    await this.onAsOfChange(value);
  }

  /** Unsaved change of a cell: ticked but not granted yet (`added`), or granted and unticked (`removed`). */
  protected changeOf(code: string | undefined): 'added' | 'removed' | null {
    if (!code) {
      return null;
    }
    const granted = this.grantedCodes().has(code);
    if (granted === this.originalCodes().has(code)) {
      return null;
    }
    return granted ? 'added' : 'removed';
  }

  /** «Roles — Edit», plus «(to add)»/«(to remove)» for a pending change (the colour is not the only cue). */
  protected cellAriaLabel(label: string, change: 'added' | 'removed' | null): string {
    if (!change) {
      return label;
    }
    this.translation();
    return `${label} (${this.transloco.translate(`access.roles.permissionMatrix.pending.${change}`)})`;
  }

  private focusField(name: string | undefined): void {
    if (!name) {
      return;
    }
    const selector =
      name === 'captionI18n' ? '[formcontrolname="captionI18n"] input[data-lang="de"]' : `[formcontrolname="${name}"]`;
    this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus();
  }

  protected errorsFor(field: string): readonly string[] {
    return this.fieldErrors()[field] ?? [];
  }
}
