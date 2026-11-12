import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import {
  NxConfirm,
  NxEmptyStateComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { PartiesFacade } from './parties.facade';
import { PartyFormComponent, PartyFormValue } from './party-form.component';
import { partyScopeKey, partyTypeKey } from './party-labels';

/** Dates are shown in this time zone. */
const DISPLAY_TIME_ZONE = 'Europe/Zurich';

/**
 * Party detail and create page (`conventions-frontend.md §4.5–§4.6`). No `id` route param
 * (`parties/new`) = create form template; an `id` = detail template: header with the legal
 * name, «type · internal/external · modified on …», status and «Deactivate» (with
 * confirmation), then the form. No tabs: a party has no audit projection yet, so there is no
 * Audit tab and a lone «General» tab would only add noise.
 *
 * Errors: `errorInterceptor` already handles 401/403/404 navigation and the 409 concurrency
 * dialog; this page turns a 400 into per-field messages (`Problem.errors`) and a 422 into the
 * translated business-rule message (`Problem.code`). Changing and deactivating both need
 * `PARTY:UPDATE` (`deactivateParty`'s `x-permission`). The API has no reactivate endpoint, so an
 * inactive party offers no status action.
 */
@Component({
  selector: 'nx-party-detail-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    NxEmptyStateComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
    PartyFormComponent,
  ],
  templateUrl: './party-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartyDetailPage implements OnInit {
  protected readonly facade = inject(PartiesFacade);
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

  protected readonly party = this.facade.selected;
  protected readonly loadError = this.facade.error;

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('PARTY:UPDATE');
  });

  /** «Organization · External · modified on 11 Nov 2026», in the active language. */
  protected readonly subtitle = computed(() => {
    const party = this.party();
    if (!party) {
      return '';
    }
    this.translation();
    const lang = this.transloco.getActiveLang();
    const date = new Intl.DateTimeFormat(lang === 'de' ? 'de-CH' : lang, {
      dateStyle: 'medium',
      timeZone: DISPLAY_TIME_ZONE,
    }).format(new Date(party.updatedAt));
    return [
      this.transloco.translate(partyTypeKey(party.partyType), {}, lang),
      this.transloco.translate(partyScopeKey(party.isInternal), {}, lang),
      this.transloco.translate('org.parties.detail.modified', { date }, lang),
    ].join(' · ');
  });

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    this.mode.set(id ? 'edit' : 'create');
    if (id) {
      void this.load();
    }
  }

  /** Loads the record (also the «Retry» of the error state) and names it in the breadcrumb. */
  protected async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    await this.facade.load(id);
    this.breadcrumb.setCurrentLabel(this.party()?.legalName || null);
  }

  protected async onSave(value: PartyFormValue): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});

    try {
      if (this.mode() === 'create') {
        const created = await this.facade.create({
          partyType: value.partyType,
          isInternal: value.isInternal,
          legalName: value.legalName,
          displayName: value.displayName || null,
          taxId: value.taxId || null,
          erpCompanyCode: value.erpCompanyCode || null,
        });
        this.notify('org.parties.created');
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          const saved = await this.facade.update(id, {
            legalName: value.legalName,
            displayName: value.displayName || null,
            taxId: value.taxId || null,
            erpCompanyCode: value.erpCompanyCode || null,
          });
          this.breadcrumb.setCurrentLabel(saved.legalName || null);
          this.notify('org.parties.saved');
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
    const party = this.party();
    if (!party) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.parties.deactivateDialog.title'),
      message: this.transloco.translate('org.parties.deactivateDialog.message', { name: party.legalName }),
      confirmLabel: this.transloco.translate('org.parties.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    try {
      await this.facade.deactivate(party.id);
      this.notify('org.parties.deactivated');
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
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
