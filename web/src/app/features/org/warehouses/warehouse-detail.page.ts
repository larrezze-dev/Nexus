import { Component, OnInit, effect, inject, signal } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxAuditTrailComponent } from '../../../shared/platform/nx-audit-trail/nx-audit-trail.component';
import { NxConfirm } from '../../../shared/ui/layout';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { WarehouseFacade } from './warehouse.facade';
import { createWarehouseForm, patchWarehouseForm, warehouseFormValue } from './warehouse-form';
import { SAME_AS_LEGAL_ENTITY_AVAILABLE, WarehouseSaveError } from './warehouse.models';
import { TranslocoService } from '@jsverse/transloco';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';

/**
 * Warehouse create/edit screen (ficha 0.4, track F): `nx-entity-picker` for
 * `locationId`/`legalEntityId`/`businessUnitId`, `nx-i18n-caption-editor` for `captionI18n`,
 * `If-Match` on save, deactivation.
 *
 * ORG-R3 ("a `businessUnitId` must belong to the same `legalEntityId`") is shown right under
 * the `businessUnitId` field via `WarehouseFacade.businessUnitError()`, not just a generic
 * toast (ficha 0.4 Gate D requires this to be visible in the form).
 *
 * `legalEntityId` is immutable once a warehouse exists (`UpdateWarehouseRequest` in the 0.4
 * contract has no `legalEntityId`): the picker for it only shows up when creating; on edit it
 * is a disabled, read-only field.
 *
 * `nx-entity-picker` is a one-shot "pick a new value" control (not a `ControlValueAccessor`):
 * on edit, the picker starts empty and `WarehouseFacade.resolvedLocationLabel()` /
 * `resolvedBusinessUnitLabel()` show the *current* value as plain text above it, so the user
 * sees what is already set before deciding whether to change it.
 */
@Component({
  selector: 'nx-warehouse-detail-page',
  imports: [
    ReactiveFormsModule,
    NxEntityPickerComponent,
    NxI18nCaptionEditorComponent,
    NxStatusBadgeComponent,
    NxAuditTrailComponent,
    NxCanDirective,
    MatTabsModule,
    MatFormFieldModule,
    MatInputModule,
    MatRadioModule,
    MatButtonModule,
    TranslocoModule,
  ],
  template: `
    <div class="nx-page-header">
      <h1>
        {{
          (isNew() ? 'warehouses.detail.title.create' : 'warehouses.detail.title.edit') | transloco
        }}
      </h1>
      @if (!isNew() && facade.selected(); as warehouse) {
        <nx-status-badge [status]="warehouse.isActive ? 'active' : 'inactive'" />
      }
    </div>

    <mat-tab-group mat-stretch-tabs="false" mat-align-tabs="start">
      <mat-tab [label]="'common.tabs.general' | transloco">
        <form [formGroup]="form" (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>{{ 'warehouses.fields.code' | transloco }}</mat-label>
            <input matInput formControlName="code" data-testid="warehouse-code-input" />
          </mat-form-field>

          <nx-i18n-caption-editor formControlName="captionI18n" />

          <fieldset class="nx-field-group" data-testid="warehouse-address">
            <legend class="nx-field-group__label">{{ 'warehouses.address.title' | transloco }}</legend>
            <mat-radio-group formControlName="addressMode">
              <mat-radio-button
                value="sameAsLegalEntity"
                [disabled]="!sameAsLegalEntityAvailable"
                data-testid="warehouse-address-same"
              >
                {{ 'warehouses.address.sameAsLegalEntity' | transloco }}
              </mat-radio-button>
              <mat-radio-button value="existingLocation" data-testid="warehouse-address-existing">
                {{ 'warehouses.address.existing' | transloco }}
              </mat-radio-button>
              <mat-radio-button value="newLocation" data-testid="warehouse-address-new">
                {{ 'warehouses.address.new' | transloco }}
              </mat-radio-button>
            </mat-radio-group>
            @if (!sameAsLegalEntityAvailable) {
              <p class="nx-field-current">{{ 'warehouses.address.sameAsLegalEntityUnavailable' | transloco }}</p>
            }

            @switch (form.controls.addressMode.value) {
              @case ('existingLocation') {
                @if (facade.resolvedLocationLabel(); as label) {
                  <p class="nx-field-current">{{ 'warehouses.fields.current' | transloco: { value: label } }}</p>
                }
                <nx-entity-picker
                  [search]="facade.searchLocations"
                  [displayWith]="facade.displayLocation"
                  (selected)="onLocationSelected($event)"
                  data-testid="warehouse-location-picker"
                />
              }
              @case ('newLocation') {
                <div formGroupName="newLocation" data-testid="warehouse-new-location">
                  @for (field of newLocationFields; track field) {
                    <mat-form-field>
                      <mat-label>{{ 'warehouses.address.fields.' + field | transloco }}</mat-label>
                      <input
                        matInput
                        [formControlName]="field"
                        [attr.data-testid]="'warehouse-new-location-' + field"
                      />
                    </mat-form-field>
                  }
                </div>
              }
            }

            @if (facade.pendingLocation(); as created) {
              <p role="status" class="nx-field-current" data-testid="warehouse-pending-location">
                {{
                  'warehouses.address.createdNotSaved'
                    | transloco: { value: facade.displayLocation(created) }
                }}
              </p>
            }
          </fieldset>

          <div class="nx-field-group">
            <span class="nx-field-group__label">{{
              'warehouses.fields.legalEntity' | transloco
            }}</span>
            @if (isNew()) {
              <nx-entity-picker
                [search]="searchLegalEntities"
                [displayWith]="facade.displayLegalEntity"
                (selected)="onLegalEntitySelected($event)"
                data-testid="warehouse-legal-entity-picker"
              />
            } @else {
              <p class="nx-field-current" data-testid="warehouse-legal-entity-current">
                {{ facade.resolvedLegalEntityLabel() }}
              </p>
            }
          </div>

          <div class="nx-field-group">
            <span class="nx-field-group__label">{{
              'warehouses.fields.businessUnit' | transloco
            }}</span>
            @if (facade.resolvedBusinessUnitLabel(); as label) {
              <p class="nx-field-current">
                {{ 'warehouses.fields.current' | transloco: { value: label } }}
              </p>
            }
            <nx-entity-picker
              [search]="searchBusinessUnits"
              [displayWith]="facade.displayBusinessUnit"
              (selected)="onBusinessUnitSelected($event)"
              data-testid="warehouse-business-unit-picker"
            />
            <button type="button" mat-button (click)="clearBusinessUnit()">
              {{ 'warehouses.fields.clear' | transloco }}
            </button>

            @if (businessUnitError(); as businessUnitError) {
              <div class="nx-field-error" role="alert" data-testid="warehouse-business-unit-error">
                {{ businessUnitError }}
              </div>
            }
          </div>

          @if (facade.error(); as loadError) {
            <p role="alert" class="nx-form-error" data-testid="warehouse-load-error">
              {{ loadError }}
              @if (!isNew() && !facade.selected()) {
                <button type="button" mat-button (click)="retryLoad()" data-testid="warehouse-retry-button">
                  {{ 'common.retry' | transloco }}
                </button>
              }
            </p>
          }
          @if (formError(); as error) {
            <p role="alert" class="nx-form-error" data-testid="warehouse-form-error">{{ error }}</p>
          }

          <div class="nx-form-actions">
            <button mat-button type="button" (click)="cancel()">
              {{ 'common.cancel' | transloco }}
            </button>
            @if (!isNew()) {
              <button
                mat-button
                color="warn"
                type="button"
                (click)="deactivate()"
                [disabled]="facade.saving() || facade.selected()?.isActive === false"
                data-testid="warehouse-deactivate-button"
              >
                {{ 'warehouses.actions.deactivate' | transloco }}
              </button>
            }
            <button
              mat-raised-button
              color="primary"
              type="submit"
              [disabled]="facade.saving()"
              data-testid="warehouse-save-button"
            >
              {{ 'common.save' | transloco }}
            </button>
          </div>
        </form>
      </mat-tab>

      @if (!isNew() && facade.selected(); as current) {
        @if (current.id === routeId()) {
          <ng-container *nxCan="'AUDIT:READ'">
            <mat-tab [label]="'common.tabs.audit' | transloco">
              <ng-template matTabContent>
                <nx-audit-trail
                  data-testid="audit-tab"
                  aggregateType="warehouse"
                  [aggregateId]="current.id"
                />
              </ng-template>
            </mat-tab>
          </ng-container>
        }
      }
    </mat-tab-group>
  `,
  styles: [
    `
      .nx-page-header {
        display: flex;
        align-items: center;
        gap: 1rem;
        margin-bottom: 1rem;
      }
      .nx-field-group {
        margin: 1rem 0;
      }
      .nx-field-group__label {
        display: block;
        font-weight: 500;
        margin-bottom: 0.25rem;
      }
      .nx-field-current {
        margin: 0 0 0.25rem;
        color: var(--mat-sys-on-surface-variant, rgba(0, 0, 0, 0.6));
      }
      .nx-field-error {
        color: var(--mat-sys-error, #b3261e);
        font-size: 0.75rem;
        margin-top: 0.25rem;
      }
    `,
  ],
})
export class WarehouseDetailPage implements OnInit {
  protected readonly facade = inject(WarehouseFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly breadcrumb = inject(NxBreadcrumbService);
  private readonly router = inject(Router);
  private readonly confirm = inject(NxConfirm);
  private readonly transloco = inject(TranslocoService);

  protected readonly isNew = signal(true);
  private warehouseId: string | null = null;
  /** Id from the route; the audit tab only shows for the record that matches it. */
  protected readonly routeId = signal<string | null>(null);
  protected readonly formError = signal<string | null>(null);
  /** ORG-R3, shown right under the business-unit field. */
  protected readonly businessUnitError = signal<string | null>(null);

  protected readonly sameAsLegalEntityAvailable = SAME_AS_LEGAL_ENTITY_AVAILABLE;
  protected readonly newLocationFields = ['code', 'name', 'street', 'postalCode', 'city', 'region', 'countryCode'] as const;

  protected readonly form = createWarehouseForm();

  protected readonly searchLegalEntities = this.facade.searchLegalEntities;

  protected readonly searchBusinessUnits = (query: string): Observable<BusinessUnitListItem[]> =>
    this.facade.searchBusinessUnits(this.form.controls.legalEntityId.value)(query);

  constructor() {
    // Breadcrumb label follows the caption in the active language, including language switches.
    effect(() => this.breadcrumb.setCurrentLabel(this.isNew() ? null : this.facade.title()));
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    this.facade.reset();
    if (id && id !== 'new') {
      this.isNew.set(false);
      this.warehouseId = id;
      this.routeId.set(id);
      void this.loadExisting(id);
    }
  }

  protected retryLoad(): void {
    if (this.warehouseId) {
      void this.loadExisting(this.warehouseId);
    }
  }

  private async loadExisting(id: string): Promise<void> {
    await this.facade.load(id);
    const warehouse = this.facade.selected();
    if (warehouse) {
      patchWarehouseForm(this.form, warehouse);
    }
  }

  protected onLocationSelected(item: LocationListItem): void {
    this.form.controls.locationId.setValue(item.id);
    this.form.controls.locationId.markAsDirty();
  }

  protected onLegalEntitySelected(item: LegalEntityListItem): void {
    this.form.controls.legalEntityId.setValue(item.id);
    // Changing the legal entity invalidates any business unit chosen for the previous one (ORG-R3).
    this.form.controls.businessUnitId.setValue(null);
  }

  protected onBusinessUnitSelected(item: BusinessUnitListItem): void {
    this.form.controls.businessUnitId.setValue(item.id);
  }

  protected clearBusinessUnit(): void {
    this.form.controls.businessUnitId.setValue(null);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.formError.set(null);
    this.businessUnitError.set(null);
    try {
      const saved = await this.facade.save(this.warehouseId, warehouseFormValue(this.form));
      if (this.isNew()) {
        void this.router.navigateByUrl(`/warehouses/${saved.id}`);
      } else {
        patchWarehouseForm(this.form, saved);
        void this.facade.load(saved.id);
      }
    } catch (error: unknown) {
      this.showSaveError(error);
    }
  }

  private showSaveError(error: unknown): void {
    const cause = error instanceof WarehouseSaveError ? error.cause : error;
    const described = this.facade.describeError(cause);
    if (described.code === 'ORG-R3') {
      this.businessUnitError.set(described.message);
    } else {
      this.formError.set(described.message);
    }
  }

  protected async deactivate(): Promise<void> {
    const warehouse = this.facade.selected();
    if (!warehouse) {
      return;
    }
    const confirmed = await this.confirm.confirm({
      title: this.transloco.translate('warehouses.deactivateDialog.title'),
      message: this.transloco.translate('warehouses.deactivateDialog.message', {
        name: this.facade.title() ?? warehouse.code,
        code: warehouse.code,
      }),
      confirmLabel: this.transloco.translate('warehouses.actions.deactivate'),
      destructive: true,
    });
    if (!confirmed) {
      return;
    }
    this.formError.set(null);
    try {
      await this.facade.deactivate(warehouse.id);
    } catch (error: unknown) {
      this.showSaveError(error);
    }
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/warehouses');
  }
}
