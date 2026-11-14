import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Currency } from '../../../api/platform/models/currency';
import {
  isConcurrencyConflict,
  localizedFieldErrors,
  problemOf,
} from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import {
  NxConfirm,
  NxDetailSkeletonComponent,
  NxEmptyStateComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { CurrenciesFacade } from './currencies.facade';
import { CurrencyFormComponent, CurrencyFormValue } from './currency-form.component';

/**
 * Currency detail (`conventions-frontend.md §4.5`): header with the currency name in the active
 * language (also the last breadcrumb), the ISO code as subtitle, the status badge, «Deactivate»
 * (destructive, with `NxConfirm`) or «Activate», and the form to rename it (the code and the
 * decimals are locked after creation). Renaming sends `PUT` with the `ETag` of the load.
 *
 * No tabs: there is no audit projection for currencies. ORG-R7 (a currency used by an active
 * group or legal entity cannot be deactivated) comes back as a 422 and is shown as an alert in
 * the user's language; a stale `ETag` (409 `CONCURRENCY`) is handled by `errorInterceptor`.
 */
@Component({
  selector: 'nx-currency-detail-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    NxDetailSkeletonComponent,
    NxEmptyStateComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
    CurrencyFormComponent,
  ],
  template: `
    @if (currency(); as current) {
      <nx-page width="form">
        <nx-page-header [breadcrumb]="true" [title]="name()" [subtitle]="current.code">
          <nx-status-badge nxHeaderStatus [status]="current.isActive ? 'active' : 'inactive'" />
          <!-- Flat @if blocks: a nested @if would not be projected into the nxHeaderActions slot. -->
          @if (canUpdate() && current.isActive) {
            <button
              nxHeaderActions
              mat-stroked-button
              type="button"
              class="nx-btn--danger"
              data-testid="currency-detail-deactivate"
              [disabled]="busy()"
              (click)="onDeactivate()"
            >
              <mat-icon aria-hidden="true">block</mat-icon>
              {{ 'org.currencies.deactivate' | transloco }}
            </button>
          }
          @if (canUpdate() && !current.isActive) {
            <button
              nxHeaderActions
              mat-stroked-button
              type="button"
              data-testid="currency-detail-activate"
              [disabled]="busy()"
              (click)="onActivate()"
            >
              <mat-icon aria-hidden="true">check_circle</mat-icon>
              {{ 'org.currencies.activate' | transloco }}
            </button>
          }
        </nx-page-header>

        @if (actionError(); as message) {
          <div class="nx-alert nx-alert--error" role="alert" data-testid="currency-detail-error">
            <mat-icon aria-hidden="true">error</mat-icon>
            <span class="nx-alert__text">{{ message }}</span>
          </div>
        }

        <p class="nx-currency-detail__hint">{{ 'org.currencies.hints.fixed' | transloco }}</p>

        <nx-currency-form
          mode="edit"
          [initial]="current"
          [saving]="renaming()"
          [readOnly]="!canUpdate()"
          [fieldErrors]="fieldErrors()"
          (save)="onRename($event)"
          (cancel)="onCancel()"
        />
      </nx-page>
    } @else if (loadError()) {
      <nx-page width="form">
        <nx-page-header [breadcrumb]="true" [title]="'org.currencies.title' | transloco" />
        <nx-empty-state kind="error" class="nx-card" data-testid="currency-detail-load-error">
          <button mat-stroked-button type="button" (click)="load()">
            <mat-icon aria-hidden="true">refresh</mat-icon>
            {{ 'common.retry' | transloco }}
          </button>
        </nx-empty-state>
      </nx-page>
    } @else {
      <nx-page width="form">
        <nx-detail-skeleton />
      </nx-page>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrencyDetailPage implements OnInit {
  private readonly facade = inject(CurrenciesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly confirm = inject(NxConfirm);
  private readonly permissions = inject(PermissionsStore);

  protected readonly currency = signal<Currency | null>(null);
  protected readonly loadError = signal(false);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly renaming = signal(false);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  private destroyed = false;

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('CURRENCY:UPDATE');
  });

  /** Currency name in the active language (`caption_i18n`: active → de → en → first). */
  protected readonly name = computed(() => {
    const currency = this.currency();
    return currency
      ? resolveCaption(currency.captionI18n, this.i18n.activeLang(), currency.code)
      : '';
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));

    // The last breadcrumb is the currency name, in the active language.
    effect(() => {
      const name = this.name();
      if (name) {
        this.breadcrumb.setCurrentLabel(name);
      }
    });
  }

  ngOnInit(): void {
    void this.load();
  }

  /** Loads the currency (also the «Retry» of the error state). */
  protected async load(): Promise<void> {
    const code = this.route.snapshot.paramMap.get('code');
    if (!code) {
      return;
    }
    this.loadError.set(false);
    try {
      const currency = await this.facade.get(code);
      if (!this.destroyed) {
        this.currency.set(currency);
      }
    } catch {
      if (this.destroyed) {
        return;
      }
      // A 404 is already sent to the «not found» page by `errorInterceptor`.
      this.currency.set(null);
      this.loadError.set(true);
      this.breadcrumb.setLoadFailed();
    }
  }

  /** Saves a new name (`PUT` with the current status and the `ETag` of the load). */
  protected async onRename(value: CurrencyFormValue): Promise<void> {
    const current = this.currency();
    if (!current) {
      return;
    }
    this.renaming.set(true);
    this.actionError.set(null);
    this.fieldErrors.set({});
    try {
      await this.facade.rename(current.code, value.captionI18n, this.facade.etag(current.code));
      this.snackBar.open(this.transloco.translate('org.currencies.renamed'), undefined, {
        duration: 4000,
      });
      await this.load();
    } catch (error: unknown) {
      this.handleRenameError(error);
    } finally {
      this.renaming.set(false);
    }
  }

  protected onCancel(): void {
    void this.router.navigateByUrl('/org/currencies');
  }

  private handleRenameError(error: unknown): void {
    const http = error as { status?: number } | undefined;
    const problem = problemOf(error);
    if (isConcurrencyConflict(error)) {
      // `errorInterceptor` already opened the concurrency dialog.
      return;
    }
    if (http?.status === 400 && problem?.errors) {
      this.fieldErrors.set(
        localizedFieldErrors(problem.errors, this.transloco.translate('shared.form.invalidValue')),
      );
      this.actionError.set(this.transloco.translate('errors.validation'));
      return;
    }
    this.actionError.set(this.i18n.translateErrorCode(http?.status === 422 ? problem?.code : undefined));
  }

  protected async onDeactivate(): Promise<void> {
    const current = this.currency();
    if (!current) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.currencies.deactivateDialog.title'),
      message: this.transloco.translate('org.currencies.deactivateDialog.message', {
        name: this.name(),
        code: current.code,
      }),
      confirmLabel: this.transloco.translate('org.currencies.deactivate'),
      destructive: true,
    });
    if (confirmed) {
      await this.setActive(current, false);
    }
  }

  protected async onActivate(): Promise<void> {
    const current = this.currency();
    if (current) {
      await this.setActive(current, true);
    }
  }

  private async setActive(current: Currency, isActive: boolean): Promise<void> {
    this.busy.set(true);
    this.actionError.set(null);
    try {
      const ok = await this.facade.toggleActive(current.code, isActive);
      if (ok) {
        this.snackBar.open(
          this.transloco.translate(
            isActive ? 'org.currencies.activated' : 'org.currencies.deactivated',
          ),
          undefined,
          { duration: 4000 },
        );
        await this.load();
        return;
      }
      const failure = this.facade.toggleError();
      this.facade.clearToggleError();
      if (failure?.problemCode === 'CONCURRENCY') {
        // `errorInterceptor` already opened the concurrency dialog.
        return;
      }
      this.actionError.set(
        failure?.problemCode === 'ORG-R7'
          ? this.transloco.translate('org.currencies.errors.inUse', { name: this.name() })
          : this.i18n.translateErrorCode(failure?.problemCode),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
