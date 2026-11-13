import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  MatAutocompleteModule,
  MatAutocompleteSelectedEvent,
} from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import {
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { countryName, countryOptions } from '../../../shared/ui/country/country-names';
import {
  NxActionBarComponent,
  NxConfirm,
  NxEmptyStateComponent,
  NxFormGridComponent,
  NxFormSectionComponent,
  NxPageComponent,
  NxPageHeaderComponent,
  NxSpanDirective,
} from '../../../shared/ui/layout';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { WarehouseFacade } from './warehouse.facade';
import {
  applyAddressMode,
  createWarehouseForm,
  patchWarehouseForm,
  warehouseFormValue,
} from './warehouse-form';
import {
  AddressMode,
  SAME_AS_LEGAL_ENTITY_AVAILABLE,
  WarehouseLocationRef,
  WarehouseSaveError,
} from './warehouse.models';

/** Dates are shown in this time zone. */
const DISPLAY_TIME_ZONE = 'Europe/Zurich';

/** Debounce of the location search box. */
const LOCATION_SEARCH_DEBOUNCE_MS = 250;

/** What the location field holds: the typed text, or the picked location. */
type LocationSearchValue = string | WarehouseLocationRef | LocationListItem;

/** Form controls in the order they appear on screen, with the element that gets the focus when invalid. */
const FIELD_ORDER: readonly { readonly path: string; readonly testId: string }[] = [
  { path: 'code', testId: 'warehouse-code-input' },
  { path: 'legalEntityId', testId: 'warehouse-legal-entity' },
  { path: 'businessUnitId', testId: 'warehouse-business-unit' },
  { path: 'captionI18n', testId: 'warehouse-caption' },
  { path: 'locationId', testId: 'warehouse-location-input' },
  { path: 'newLocation.name', testId: 'warehouse-new-location-name' },
  { path: 'newLocation.code', testId: 'warehouse-new-location-code' },
  { path: 'newLocation.street', testId: 'warehouse-new-location-street' },
  { path: 'newLocation.postalCode', testId: 'warehouse-new-location-postalCode' },
  { path: 'newLocation.city', testId: 'warehouse-new-location-city' },
  { path: 'newLocation.region', testId: 'warehouse-new-location-region' },
  { path: 'newLocation.countryCode', testId: 'warehouse-new-location-countryCode' },
];

/**
 * Warehouse detail and create page (`conventions-frontend.md §4.5–§4.6`), same structure as the
 * legal-entity and business-unit pages. No `id` route param (`warehouses/new`) = create form
 * template; an `id` = detail template: header with the name (`captionI18n` in the active
 * language, also in the breadcrumb, following a language switch), «code · modified on …», status
 * and «Deactivate», and the General · Audit tabs.
 *
 * Form sections: «Identification» (code, legal entity, business unit), «Name in each language»
 * (`captionI18n` editor) and «Warehouse address» with the three address modes (the «same as the
 * legal entity» one is shown disabled with its note: it needs an API that does not exist yet).
 * The legal entity is fixed once the warehouse exists (lock + hint). The business unit only
 * offers units of the chosen legal entity; an ORG-R3 rejection is shown under that field.
 *
 * Saving with a new address is two calls (location, then warehouse): the page shows which step
 * runs, and if the second one fails it says that the location already exists and offers «Retry»
 * (the facade reuses that location). The API has no reactivate endpoint, so an inactive
 * warehouse offers no status action.
 */
@Component({
  selector: 'nx-warehouse-detail-page',
  standalone: true,
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslocoModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    MatSelectModule,
    MatTabsModule,
    NxActionBarComponent,
    NxAuditTrailComponent,
    NxCanDirective,
    NxEmptyStateComponent,
    NxFormGridComponent,
    NxFormSectionComponent,
    NxI18nCaptionEditorComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    NxSpanDirective,
    NxStatusBadgeComponent,
  ],
  templateUrl: './warehouse-detail.page.html',
  styleUrl: './warehouse-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WarehouseDetailPage implements OnInit {
  protected readonly facade = inject(WarehouseFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly confirm = inject(NxConfirm);
  private readonly snackBar = inject(MatSnackBar);
  private readonly permissions = inject(PermissionsStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Emits once the active language's file is loaded (and on every switch): texts built in TS follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  private readonly saveAlert = viewChild<ElementRef<HTMLElement>>('saveAlert');

  protected readonly mode = signal<'create' | 'edit'>('create');
  /** Id from the route; the audit tab only shows for the record that matches it. */
  protected readonly routeId = signal<string | null>(null);

  protected readonly form = createWarehouseForm();
  protected readonly sameAsLegalEntityAvailable = SAME_AS_LEGAL_ENTITY_AVAILABLE;

  /** Translated message of the last failed save or deactivation (alert above the form). */
  protected readonly saveError = signal<string | null>(null);
  /** The last save failed after creating the new location: say so and offer «Retry». */
  protected readonly locationCreated = signal(false);
  /** ORG-R3 message, shown under the business-unit field. */
  protected readonly businessUnitError = signal<string | null>(null);
  /** Number of invalid fields after a submit attempt (alert above the form); 0 = no alert. */
  protected readonly invalidCount = signal(0);

  protected readonly selected = this.facade.selected;

  protected readonly canUpdate = computed(() => {
    this.permissions.permissions(); // re-evaluate when permissions change
    return this.permissions.has('ORG_STRUCTURE:UPDATE');
  });

  /** Edit mode without the permission to change: every field read-only and Save disabled. */
  protected readonly readOnly = computed(() => this.mode() === 'edit' && !this.canUpdate());

  /** Record name: the caption in the active language (→ de → en → first), the code without one. */
  protected readonly name = computed(() => this.facade.title() ?? '');

  /** «WH-CH-01 · modified on 11 Nov 2026», in the active language. */
  protected readonly subtitle = computed(() => {
    const warehouse = this.selected();
    if (!warehouse) {
      return '';
    }
    this.translation();
    const lang = this.transloco.getActiveLang();
    const date = new Intl.DateTimeFormat(lang === 'de' ? 'de-CH' : lang, {
      dateStyle: 'medium',
      timeZone: DISPLAY_TIME_ZONE,
    }).format(new Date(warehouse.updatedAt));
    return `${warehouse.code} · ${this.transloco.translate('org.warehouses.detail.modified', { date }, lang)}`;
  });

  protected readonly addressMode = toSignal(this.form.controls.addressMode.valueChanges, {
    initialValue: this.form.controls.addressMode.value,
  });
  private readonly legalEntityId = toSignal(this.form.controls.legalEntityId.valueChanges, {
    initialValue: this.form.controls.legalEntityId.value,
  });
  private readonly businessUnitId = toSignal(this.form.controls.businessUnitId.valueChanges, {
    initialValue: this.form.controls.businessUnitId.value,
  });

  /** Reference lists for the legal-entity and business-unit selects; empty if they cannot be read. */
  private readonly legalEntities = toSignal(
    this.facade.searchLegalEntities('').pipe(catchError(() => of<LegalEntityListItem[]>([]))),
    { initialValue: [] as LegalEntityListItem[] },
  );
  private readonly businessUnits = toSignal(
    this.facade
      .searchBusinessUnits(null)('')
      .pipe(catchError(() => of<BusinessUnitListItem[]>([]))),
    { initialValue: [] as BusinessUnitListItem[] },
  );

  /** Create: the active legal entities. Edit: only the warehouse's own (the field is locked). */
  protected readonly legalEntityChoices = computed<
    readonly { id: string; code: string; legalName: string }[]
  >(() => {
    if (this.mode() === 'edit') {
      const ref = this.facade.legalEntityRef();
      return ref ? [{ id: ref.id, code: ref.code, legalName: ref.legalName ?? '' }] : [];
    }
    return this.legalEntities()
      .filter((legalEntity) => legalEntity.isActive)
      .map((legalEntity) => ({
        id: legalEntity.id,
        code: legalEntity.code,
        legalName: legalEntity.legalName,
      }));
  });

  protected readonly selectedLegalEntity = computed(
    () => this.legalEntityChoices().find((option) => option.id === this.legalEntityId()) ?? null,
  );

  /** Active business units of the chosen legal entity, plus the current one if it is no longer active. */
  protected readonly businessUnitChoices = computed<
    readonly { id: string; code: string; label: string }[]
  >(() => {
    const lang = this.i18n.activeLang();
    const legalEntityId = this.legalEntityId();
    const current = this.mode() === 'edit' ? this.facade.businessUnitRef() : null;
    const units = this.businessUnits()
      .filter(
        (unit) =>
          unit.legalEntityId === legalEntityId && (unit.isActive || unit.id === current?.id),
      )
      .map((unit) => ({
        id: unit.id,
        code: unit.code,
        label: resolveCaption(unit.captionI18n, lang, unit.code),
      }));
    if (current && !units.some((unit) => unit.id === current.id)) {
      units.unshift({
        id: current.id,
        code: current.code,
        label: resolveCaption(current.captionI18n, lang, current.code),
      });
    }
    return units;
  });

  protected readonly selectedBusinessUnit = computed(
    () => this.businessUnitChoices().find((option) => option.id === this.businessUnitId()) ?? null,
  );

  /** Countries by name in the UI language (the value stays the ISO code the API expects). */
  protected readonly countries = computed(() => countryOptions(this.i18n.activeLang()));

  /** Location field: free text to search, or the picked location (shown as «code — name»). */
  protected readonly locationSearch = new FormControl<LocationSearchValue>('', {
    nonNullable: true,
  });
  private readonly locationQuery = new Subject<string>();
  protected readonly locationOptions = toSignal(
    this.locationQuery.pipe(
      startWith(''),
      debounceTime(LOCATION_SEARCH_DEBOUNCE_MS),
      distinctUntilChanged(),
      switchMap((query) =>
        this.facade.searchLocations(query).pipe(catchError(() => of<LocationListItem[]>([]))),
      ),
    ),
    { initialValue: [] as LocationListItem[] },
  );
  /**
   * The location field is in error when no location is picked (`locationId`), not when its text
   * is empty: typed text that matches nothing is not a location.
   */
  protected readonly locationErrorState: ErrorStateMatcher = {
    isErrorState: () => {
      const locationId = this.form.controls.locationId;
      return (
        locationId.enabled &&
        locationId.invalid &&
        (locationId.touched || this.locationSearch.touched)
      );
    },
  };

  protected readonly displayLocation = (value: LocationSearchValue | null): string =>
    !value ? '' : typeof value === 'string' ? value : this.facade.displayLocation(value);

  /** Progress text of a running save («Creating location…», «Saving warehouse…»); `null` when idle. */
  protected readonly saveProgressKey = computed(() => {
    switch (this.facade.saveStep()) {
      case 'creatingLocation':
        return 'org.warehouses.progress.creatingLocation';
      case 'savingWarehouse':
        return 'org.warehouses.progress.savingWarehouse';
      default:
        return null;
    }
  });

  constructor() {
    // The breadcrumb names the record and follows a language switch (the name is a caption).
    effect(() => {
      const name = this.name();
      if (this.mode() === 'edit' && name) {
        this.breadcrumb.setCurrentLabel(name);
      }
    });

    // Typing in the location field searches and drops the previous choice.
    this.locationSearch.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      if (typeof value !== 'string') {
        return;
      }
      const locationId = this.form.controls.locationId;
      if (locationId.value !== null) {
        locationId.setValue(null);
        locationId.markAsDirty();
      }
      this.locationQuery.next(value.trim());
    });

    // Read-only (no permission): every control is disabled; otherwise only what applies.
    effect(() => this.syncEnabledState(this.readOnly(), this.legalEntityId(), this.addressMode()));
  }

  ngOnInit(): void {
    this.facade.reset();
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.mode.set('edit');
      this.routeId.set(id);
      void this.load();
    }
  }

  /** Loads the record (also the «Retry» of the error state) and fills the form. */
  protected async load(): Promise<void> {
    const id = this.routeId();
    if (!id) {
      return;
    }
    await this.facade.load(id);
    const warehouse = this.facade.selected();
    if (warehouse) {
      patchWarehouseForm(this.form, warehouse);
      // The current location, by name, inside the location field.
      this.locationSearch.setValue(this.facade.locationRef() ?? '', { emitEvent: false });
      this.syncEnabledState(this.readOnly(), warehouse.legalEntityId, 'existingLocation');
    }
  }

  protected onLocationSelected(event: MatAutocompleteSelectedEvent): void {
    const item = event.option.value as LocationListItem;
    this.form.controls.locationId.setValue(item.id);
    this.form.controls.locationId.markAsDirty();
  }

  /** A business unit of the previous legal entity would break ORG-R3: it is cleared. */
  protected onLegalEntityChange(): void {
    this.form.controls.businessUnitId.setValue(null);
  }

  protected clearBusinessUnit(event: Event): void {
    event.stopPropagation();
    this.form.controls.businessUnitId.setValue(null);
    this.form.controls.businessUnitId.markAsDirty();
  }

  protected countryLabel(code: string): string {
    return countryName(code, this.i18n.activeLang());
  }

  protected async save(): Promise<void> {
    if (this.facade.saving() || this.readOnly()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.locationSearch.markAsTouched();
      const invalid = FIELD_ORDER.filter((field) => this.isInvalid(this.form.get(field.path)));
      this.invalidCount.set(invalid.length);
      this.focusTestId(invalid[0]?.testId);
      return;
    }
    this.invalidCount.set(0);
    this.saveError.set(null);
    this.locationCreated.set(false);
    this.businessUnitError.set(null);
    const id = this.mode() === 'edit' ? this.routeId() : null;
    try {
      const saved = await this.facade.save(id, warehouseFormValue(this.form));
      if (this.mode() === 'create') {
        this.notify('org.warehouses.created');
        void this.router.navigateByUrl(`/warehouses/${saved.id}`);
      } else {
        this.notify('org.warehouses.saved');
        // Reload: a new address becomes the warehouse's location, shown by name.
        await this.load();
      }
    } catch (error: unknown) {
      this.showSaveError(error);
    }
  }

  protected async deactivate(): Promise<void> {
    const warehouse = this.selected();
    if (!warehouse) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('org.warehouses.deactivateDialog.title'),
      message: this.transloco.translate('org.warehouses.deactivateDialog.message', {
        name: this.name() || warehouse.code,
        code: warehouse.code,
      }),
      confirmLabel: this.transloco.translate('org.warehouses.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    this.saveError.set(null);
    this.locationCreated.set(false);
    try {
      await this.facade.deactivate(warehouse.id);
      this.notify('org.warehouses.deactivated');
    } catch (error: unknown) {
      this.showSaveError(error);
    }
  }

  /** Cancel: back to the list, nothing is saved. */
  protected cancel(): void {
    void this.router.navigateByUrl('/warehouses');
  }

  private showSaveError(error: unknown): void {
    const cause = error instanceof WarehouseSaveError ? error.cause : error;
    const http = cause as { status?: number; error?: unknown } | null;
    if (http?.status === 409 && isProblem(http.error) && http.error.type === 'concurrency') {
      // `errorInterceptor` already opened the concurrency dialog for a stale version.
      return;
    }
    const described = this.facade.describeError(cause);
    if (described.code === 'ORG-R3') {
      this.businessUnitError.set(described.message);
      const control = this.form.controls.businessUnitId;
      control.setErrors({ ...(control.errors ?? {}), orgR3: true });
      control.markAsTouched();
      this.focusTestId('warehouse-business-unit');
      return;
    }
    this.saveError.set(described.message);
    this.locationCreated.set(error instanceof WarehouseSaveError && error.locationCreated);
    // Bring the alert into view: the button that failed is at the bottom of a long form.
    setTimeout(() => this.saveAlert()?.nativeElement.focus());
  }

  /**
   * Enables only the controls that apply: all disabled when read-only; otherwise the address
   * controls of the chosen mode (`applyAddressMode`), the legal entity on create only, and the
   * business unit once a legal entity is chosen.
   */
  private syncEnabledState(readOnly: boolean, legalEntityId: string, mode: AddressMode): void {
    const controls = this.form.controls;
    if (readOnly) {
      this.form.disable({ emitEvent: false });
      this.locationSearch.disable({ emitEvent: false });
      return;
    }
    for (const control of [controls.code, controls.captionI18n, controls.addressMode]) {
      this.setEnabled(control, true);
    }
    this.setEnabled(controls.legalEntityId, this.mode() === 'create');
    this.setEnabled(controls.businessUnitId, !!legalEntityId);
    applyAddressMode(this.form, mode);
    this.setEnabled(this.locationSearch, mode === 'existingLocation');
  }

  private setEnabled(control: AbstractControl, enabled: boolean): void {
    if (enabled && control.disabled) {
      control.enable({ emitEvent: false });
    } else if (!enabled && control.enabled) {
      control.disable({ emitEvent: false });
    }
  }

  private isInvalid(control: AbstractControl | null): boolean {
    return !!control && control.enabled && control.invalid;
  }

  /** Focuses the field (or, for the caption editor, its German input) with that `data-testid`. */
  private focusTestId(testId: string | undefined): void {
    if (!testId) {
      return;
    }
    setTimeout(() => {
      const element = this.host.nativeElement.querySelector<HTMLElement>(
        `[data-testid="${testId}"]`,
      );
      const target = element?.matches('input, textarea, mat-select, [tabindex]')
        ? element
        : element?.querySelector<HTMLElement>('input[data-lang="de"], input, mat-select');
      target?.focus();
    });
  }

  private notify(key: string): void {
    this.snackBar.open(this.transloco.translate(key), undefined, { duration: 4000 });
  }
}
