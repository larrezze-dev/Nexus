import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { LegalEntityFormComponent, LegalEntityFormValue } from './legal-entity-form.component';
import { LegalEntitiesFacade } from './legal-entities.facade';

/**
 * `LegalEntity` detail/create page (`conventions-frontend.md §4`). No `id`
 * route param (mounted at `legal-entities/new`) means create mode; an `id`
 * param means edit mode and triggers `facade.load(id)`.
 *
 * Error handling follows the ficha's hand-off with `errorInterceptor`: 401/
 * 403/404 navigation and the 409-concurrency dialog are already handled
 * there, so this page only needs to turn a 400 into per-field messages
 * (`Problem.errors`) and a 422 into a translated business-rule message
 * (`Problem.code` via `NxI18nService.translateErrorCode()`).
 */
@Component({
  selector: 'nx-legal-entity-detail-page',
  standalone: true,
  imports: [TranslocoModule, MatButtonModule, NxCanDirective, NxStatusBadgeComponent, LegalEntityFormComponent],
  templateUrl: './legal-entity-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityDetailPage implements OnInit {
  protected readonly facade = inject(LegalEntitiesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly entity = this.facade.selected;
  protected readonly loading = this.facade.loading;

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.mode.set('edit');
      void this.facade.load(id);
    } else {
      this.mode.set('create');
    }
  }

  protected async onSave(value: LegalEntityFormValue): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});

    try {
      if (this.mode() === 'create') {
        const created = await this.facade.create({
          legalName: value.legalName,
          displayName: value.displayName || null,
          groupId: value.groupId,
          code: value.code,
          currencyCode: value.currencyCode,
          countryCode: value.countryCode,
        });
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          await this.facade.update(id, {
            code: value.code,
            currencyCode: value.currencyCode,
            countryCode: value.countryCode,
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
    const confirmed = globalThis.confirm(this.transloco.translate('org.legalEntities.confirmDeactivate'));
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
}
