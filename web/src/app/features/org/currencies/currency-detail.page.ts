import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { Currency } from '../../../api/platform/models/currency';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import {
  NxConfirm,
  NxEmptyStateComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxPageComponent,
  NxPageHeaderComponent,
} from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { CurrenciesFacade } from './currencies.facade';

/**
 * Currency detail (`conventions-frontend.md §4.5`): header with the currency name in the active
 * language (also the last breadcrumb), the ISO code as subtitle, the status badge and the only
 * write the API offers — «Deactivate» (destructive, with `NxConfirm`) or «Activate».
 *
 * Every field is fixed by the seed (the API only changes `isActive`), so the page shows them
 * read-only with a lock and has no «Save» bar. No tabs: there is no audit projection for
 * currencies. ORG-R7 (a currency used by an active group or legal entity cannot be deactivated)
 * comes back as a 422 and is shown as an alert in the user's language.
 */
@Component({
  selector: 'nx-currency-detail-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    NxEmptyStateComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
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

        <nx-form-section
          [title]="'org.currencies.sections.currency' | transloco"
          [hint]="'org.currencies.hints.fixed' | transloco"
        >
          <nx-form-grid>
            <mat-form-field nxSpan="s" subscriptSizing="dynamic" class="nx-field--locked">
              <mat-label>{{ 'org.currencies.fields.code' | transloco }}</mat-label>
              <input
                matInput
                class="nx-mono"
                [value]="current.code"
                disabled
                data-testid="currency-detail-code"
              />
              <mat-icon matSuffix aria-hidden="true">lock</mat-icon>
            </mat-form-field>
            <mat-form-field nxSpan="l" subscriptSizing="dynamic" class="nx-field--locked">
              <mat-label>{{ 'org.currencies.fields.caption' | transloco }}</mat-label>
              <input matInput [value]="name()" disabled data-testid="currency-detail-caption" />
              <mat-icon matSuffix aria-hidden="true">lock</mat-icon>
            </mat-form-field>
            <mat-form-field nxSpan="s" subscriptSizing="dynamic" class="nx-field--locked">
              <mat-label>{{ 'org.currencies.fields.minorUnits' | transloco }}</mat-label>
              <input
                matInput
                [value]="current.minorUnits"
                disabled
                data-testid="currency-detail-minor-units"
              />
              <mat-icon matSuffix aria-hidden="true">lock</mat-icon>
              <mat-hint>{{ 'org.currencies.hints.minorUnits' | transloco }}</mat-hint>
            </mat-form-field>
          </nx-form-grid>
        </nx-form-section>
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
        <nx-page-header [breadcrumb]="true" [title]="'org.currencies.loading' | transloco" />
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
  private readonly confirm = inject(NxConfirm);
  private readonly permissions = inject(PermissionsStore);

  protected readonly currency = signal<Currency | null>(null);
  protected readonly loadError = signal(false);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

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
      this.currency.set(await this.facade.get(code));
    } catch {
      // A 404 is already sent to the «not found» page by `errorInterceptor`.
      this.currency.set(null);
      this.loadError.set(true);
    }
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
