import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import {
  NxConfirm,
  NxEmptyStateComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { BusinessUnitFormComponent, BusinessUnitFormValue } from './business-unit-form.component';
import { BusinessUnitLegalEntityOption, BusinessUnitsFacade } from './business-units.facade';

/** Dates are shown in this time zone. */
const DISPLAY_TIME_ZONE = 'Europe/Zurich';

/**
 * Business-unit detail and create page (`conventions-frontend.md §4.5–§4.6`), same structure as
 * the legal-entity page. No `id` route param (`business-units/new`) = create form template; an
 * `id` = detail template: header with the name (`captionI18n` in the active language, also in
 * the breadcrumb and following a language switch), «code · modified on …», status and
 * «Deactivate», and the General · Audit tabs.
 *
 * Errors: `errorInterceptor` already handles 401/403/404 navigation and the 409 concurrency
 * dialog; this page turns a 400 into per-field messages (`Problem.errors`) and a 422 into the
 * translated business-rule message (`Problem.code`). The API has no reactivate endpoint, so an
 * inactive unit offers no status action.
 */
@Component({
  selector: 'nx-business-unit-detail-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    NxCanDirective,
    NxAuditTrailComponent,
    NxEmptyStateComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
    BusinessUnitFormComponent,
  ],
  templateUrl: './business-unit-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitDetailPage implements OnInit {
  protected readonly facade = inject(BusinessUnitsFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly permissions = inject(PermissionsStore);

  /** Emits once the active language's file is loaded (and on every switch): texts built in TS follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  protected readonly legalEntities = signal<readonly BusinessUnitLegalEntityOption[]>([]);

  protected readonly entity = this.facade.selected;
  protected readonly party = this.facade.party;
  protected readonly loading = this.facade.loading;
  protected readonly loadError = this.facade.error;

  /** Record name: the caption in the active language (→ de → en → first), the code without one. */
  protected readonly name = computed(() => {
    const entity = this.entity();
    return entity ? resolveCaption(entity.captionI18n, this.i18n.activeLang(), entity.code) : '';
  });

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('ORG_STRUCTURE:UPDATE');
  });

  /** «BU-CH-EVENTS · modified on 11 Nov 2026», in the active language. */
  protected readonly subtitle = computed(() => {
    const entity = this.entity();
    if (!entity) {
      return '';
    }
    this.translation();
    const lang = this.transloco.getActiveLang();
    const date = new Intl.DateTimeFormat(lang === 'de' ? 'de-CH' : lang, {
      dateStyle: 'medium',
      timeZone: DISPLAY_TIME_ZONE,
    }).format(new Date(entity.updatedAt));
    return `${entity.code} · ${this.transloco.translate('org.businessUnits.detail.modified', { date }, lang)}`;
  });

  constructor() {
    // The breadcrumb names the record and follows a language switch (the name is a caption).
    effect(() => {
      const name = this.name();
      if (this.mode() === 'edit' && name) {
        this.breadcrumb.setCurrentLabel(name);
      }
    });
  }

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    this.mode.set(id ? 'edit' : 'create');
    if (id) {
      void this.load();
    }
    void this.loadOptions();
  }

  /** Loads the record (also the «Retry» of the error state). */
  protected async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      await this.facade.load(id);
    }
  }

  protected async onSave(value: BusinessUnitFormValue): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});

    try {
      if (this.mode() === 'create') {
        const created = await this.facade.create({
          legalName: value.legalName,
          displayName: value.displayName || null,
          legalEntityId: value.legalEntityId,
          code: value.code,
          captionI18n: value.captionI18n,
          costCenterCode: value.costCenterCode || null,
        });
        this.notify('org.businessUnits.created');
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          await this.facade.update(id, {
            code: value.code,
            captionI18n: value.captionI18n,
            costCenterCode: value.costCenterCode || null,
          });
          this.notify('org.businessUnits.saved');
        }
      }
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  /** Cancel: back to the list, nothing is saved. */
  protected onCancel(): void {
    void this.router.navigate(['..'], { relativeTo: this.route });
  }

  protected async onDeactivate(): Promise<void> {
    const entity = this.entity();
    if (!entity) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.businessUnits.deactivateDialog.title'),
      message: this.transloco.translate('org.businessUnits.deactivateDialog.message', {
        name: this.name(),
        code: entity.code,
      }),
      confirmLabel: this.transloco.translate('org.businessUnits.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.facade.deactivate(entity.id);
      this.notify('org.businessUnits.deactivated');
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  /** Legal-entity choices; on failure the select stays empty (edit: the locked field is blank). */
  private async loadOptions(): Promise<void> {
    try {
      this.legalEntities.set(await this.facade.legalEntityOptions());
    } catch {
      this.legalEntities.set([]);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
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
}
