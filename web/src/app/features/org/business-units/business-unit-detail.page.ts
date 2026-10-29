import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LegalEntitiesFacade } from '../legal-entities/legal-entities.facade';
import { BusinessUnitFormComponent, BusinessUnitFormValue } from './business-unit-form.component';
import { BusinessUnitsFacade } from './business-units.facade';

/**
 * `BusinessUnit` detail/create page — same structure as
 * `LegalEntityDetailPage`. In edit mode, besides loading the `BusinessUnit`
 * itself, it also loads its linked `LegalEntity` (via
 * `LegalEntitiesFacade.load`) purely to show that legal entity's `code` as
 * the read-only `legalEntityDisplay` the form needs (`legalEntityId` is
 * immutable after creation, so the picker is only shown on create).
 */
@Component({
  selector: 'nx-business-unit-detail-page',
  standalone: true,
  imports: [TranslocoModule, MatButtonModule, NxCanDirective, NxStatusBadgeComponent, BusinessUnitFormComponent],
  templateUrl: './business-unit-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitDetailPage implements OnInit {
  protected readonly facade = inject(BusinessUnitsFacade);
  private readonly legalEntitiesFacade = inject(LegalEntitiesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  protected readonly legalEntityDisplay = signal('');

  protected readonly entity = this.facade.selected;
  protected readonly loading = this.facade.loading;

  protected readonly legalEntitySearch = (query: string): Observable<LegalEntityListItem[]> =>
    this.legalEntitiesFacade.search(query);

  /**
   * Tracks `ngOnInit`'s fire-and-forget load chain (`facade.load` then,
   * in edit mode, `legalEntitiesFacade.load`) so tests can `await` it
   * directly instead of guessing how many microtask ticks `whenStable()`
   * needs in this zoneless app (no `zone.js` dependency — plain
   * `async`/`await` on a mocked promise isn't tracked as an Angular
   * "pending task", so `ComponentFixture.whenStable()` can resolve before
   * the chain actually finishes).
   */
  protected initialized: Promise<void> = Promise.resolve();

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.mode.set('edit');
      this.initialized = this.loadForEdit(id);
    } else {
      this.mode.set('create');
    }
  }

  private async loadForEdit(id: string): Promise<void> {
    await this.facade.load(id);
    const legalEntityId = this.entity()?.legalEntityId;
    if (legalEntityId) {
      await this.legalEntitiesFacade.load(legalEntityId);
      this.legalEntityDisplay.set(this.legalEntitiesFacade.selected()?.code ?? '');
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
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          await this.facade.update(id, {
            code: value.code,
            captionI18n: value.captionI18n,
            costCenterCode: value.costCenterCode || null,
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
    const confirmed = globalThis.confirm(this.transloco.translate('org.businessUnits.confirmDeactivate'));
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
