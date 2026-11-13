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
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxEmptyStateComponent, NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { LocationFormComponent, LocationFormValue } from './location-form.component';
import { LocationFacade } from './location.facade';

/** Dates are shown in this time zone. */
const DISPLAY_TIME_ZONE = 'Europe/Zurich';

/**
 * Location detail and create page (`conventions-frontend.md §4.5–§4.6`), same structure as the
 * other org pages. Route `locations/new` (no `id` param) = create form template; an `id` =
 * detail template: header with the location name (also in the breadcrumb), «code · modified on
 * …» (just the date when the location has no code), the status badge, and the General tab.
 *
 * No header action: the API has no deactivate/reactivate endpoint for locations (0.4 contract).
 * No Audit tab: there is no audit projection for locations yet, so `nx-audit-trail` would have
 * nothing to show — add the tab when the projection exists.
 *
 * Errors: `errorInterceptor` already handles 401/403/404 navigation and the 409 concurrency
 * dialog; this page turns a 400 into per-field messages (`Problem.errors`) and a 422 into the
 * translated business-rule message (`Problem.code`).
 */
@Component({
  selector: 'nx-location-detail-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    NxEmptyStateComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxStatusBadgeComponent,
    LocationFormComponent,
  ],
  templateUrl: './location-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationDetailPage implements OnInit {
  protected readonly facade = inject(LocationFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly permissions = inject(PermissionsStore);

  /** Emits once the active language's file is loaded (and on every switch): texts built in TS follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  protected readonly mode = signal<'create' | 'edit'>('create');
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly location = this.facade.selected;
  protected readonly loadError = this.facade.error;

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('ORG_STRUCTURE:UPDATE');
  });

  /** «LOC-CH-01 · modified on 11 Nov 2026» in the active language; only the date without a code. */
  protected readonly subtitle = computed(() => {
    const location = this.location();
    if (!location) {
      return '';
    }
    this.translation();
    const lang = this.transloco.getActiveLang();
    const date = new Intl.DateTimeFormat(lang === 'de' ? 'de-CH' : lang, {
      dateStyle: 'medium',
      timeZone: DISPLAY_TIME_ZONE,
    }).format(new Date(location.updatedAt));
    const modified = this.transloco.translate('org.locations.detail.modified', { date }, lang);
    return location.code ? `${location.code} · ${modified}` : modified;
  });

  constructor() {
    // The last breadcrumb is the location name (updated after a rename too).
    effect(() => {
      const name = this.location()?.name;
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
  }

  /** Loads the record (also the «Retry» of the error state). */
  protected async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      await this.facade.load(id);
    }
  }

  protected async onSave(value: LocationFormValue): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});

    try {
      if (this.mode() === 'create') {
        const created = await this.facade.create(value);
        this.notify('org.locations.created');
        void this.router.navigate(['/locations', created.id]);
      } else {
        const id = this.route.snapshot.paramMap.get('id');
        if (id) {
          await this.facade.update(id, value);
          this.notify('org.locations.saved');
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
    void this.router.navigate(['/locations']);
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
