import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable } from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { emptyI18nText, I18nText } from '../../../shared/ui/i18n-text.model';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import { NxI18nCaptionEditorComponent } from '../../../shared/ui/nx-i18n-caption-editor/nx-i18n-caption-editor.component';
import { NxStatusBadgeComponent } from '../../../shared/ui/nx-status-badge/nx-status-badge.component';
import { WarehouseFacade } from './warehouse.facade';

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
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    TranslocoModule,
  ],
  template: `
    <div class="nx-page-header">
      <h1>{{ (isNew() ? 'warehouses.detail.title.create' : 'warehouses.detail.title.edit') | transloco }}</h1>
      @if (!isNew() && facade.selected(); as warehouse) {
        <nx-status-badge [status]="warehouse.isActive ? 'active' : 'inactive'" />
      }
    </div>

    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-form-field>
        <mat-label>{{ 'warehouses.fields.code' | transloco }}</mat-label>
        <input matInput formControlName="code" data-testid="warehouse-code-input" />
      </mat-form-field>

      <nx-i18n-caption-editor formControlName="captionI18n" />

      <div class="nx-field-group">
        <span class="nx-field-group__label">{{ 'warehouses.fields.location' | transloco }}</span>
        @if (facade.resolvedLocationLabel(); as label) {
          <p class="nx-field-current">{{ 'warehouses.fields.current' | transloco: { value: label } }}</p>
        }
        <nx-entity-picker [search]="searchLocations" [displayWith]="displayLocation" (selected)="onLocationSelected($event)" />
      </div>

      <div class="nx-field-group">
        <span class="nx-field-group__label">{{ 'warehouses.fields.legalEntity' | transloco }}</span>
        @if (isNew()) {
          <nx-entity-picker
            [search]="searchLegalEntities"
            [displayWith]="displayCode"
            (selected)="onLegalEntitySelected($event)"
            data-testid="warehouse-legal-entity-picker"
          />
        } @else {
          <p class="nx-field-current" data-testid="warehouse-legal-entity-current">
            {{ facade.resolvedLegalEntityLabel() ?? form.controls.legalEntityId.value }}
          </p>
        }
      </div>

      <div class="nx-field-group">
        <span class="nx-field-group__label">{{ 'warehouses.fields.businessUnit' | transloco }}</span>
        @if (facade.resolvedBusinessUnitLabel(); as label) {
          <p class="nx-field-current">{{ 'warehouses.fields.current' | transloco: { value: label } }}</p>
        }
        <nx-entity-picker
          [search]="searchBusinessUnits"
          [displayWith]="displayCode"
          (selected)="onBusinessUnitSelected($event)"
          data-testid="warehouse-business-unit-picker"
        />
        <button type="button" mat-button (click)="clearBusinessUnit()">{{ 'warehouses.fields.clear' | transloco }}</button>

        @if (facade.businessUnitError(); as businessUnitError) {
          <div class="nx-field-error" role="alert" data-testid="warehouse-business-unit-error">
            {{ businessUnitError }}
          </div>
        }
      </div>

      @if (facade.error(); as error) {
        <p role="alert" class="nx-form-error" data-testid="warehouse-form-error">{{ error }}</p>
      }

      <div class="nx-form-actions">
        <button mat-button type="button" (click)="cancel()">{{ 'common.cancel' | transloco }}</button>
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
        <button mat-raised-button color="primary" type="submit" [disabled]="facade.saving()" data-testid="warehouse-save-button">
          {{ 'common.save' | transloco }}
        </button>
      </div>
    </form>
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
  private readonly router = inject(Router);

  protected readonly isNew = signal(true);
  private warehouseId: string | null = null;

  protected readonly form = new FormGroup({
    locationId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    legalEntityId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    businessUnitId: new FormControl<string | null>(null),
    code: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    captionI18n: new FormControl<I18nText>(emptyI18nText(), { nonNullable: true }),
  });

  protected readonly searchLocations = this.facade.searchLocations;
  protected readonly searchLegalEntities = this.facade.searchLegalEntities;

  protected readonly searchBusinessUnits = (query: string): Observable<BusinessUnitListItem[]> =>
    this.facade.searchBusinessUnits(this.form.controls.legalEntityId.value)(query);

  protected readonly displayCode = (item: { code: string }): string => item.code;
  protected readonly displayLocation = (item: LocationListItem): string => (item.code ? `${item.code} — ${item.name}` : item.name);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isNew.set(false);
      this.warehouseId = id;
      void this.loadExisting(id);
    } else {
      this.facade.reset();
    }
  }

  private async loadExisting(id: string): Promise<void> {
    await this.facade.load(id);
    const warehouse = this.facade.selected();
    if (!warehouse) {
      return;
    }
    this.form.patchValue({
      locationId: warehouse.locationId,
      legalEntityId: warehouse.legalEntityId,
      businessUnitId: warehouse.businessUnitId ?? null,
      code: warehouse.code,
      captionI18n: warehouse.captionI18n as I18nText,
    });
    // ORG-R3/contract: `legalEntityId` is immutable once a warehouse exists.
    this.form.controls.legalEntityId.disable();
  }

  protected onLocationSelected(item: LocationListItem): void {
    this.form.controls.locationId.setValue(item.id);
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
    const value = this.form.getRawValue();
    if (this.isNew()) {
      const created = await this.facade.create({
        locationId: value.locationId,
        legalEntityId: value.legalEntityId,
        businessUnitId: value.businessUnitId,
        code: value.code,
        captionI18n: value.captionI18n,
      });
      if (created) {
        void this.router.navigateByUrl(`/warehouses/${created.id}`);
      }
    } else if (this.warehouseId) {
      const updated = await this.facade.update(this.warehouseId, {
        locationId: value.locationId,
        businessUnitId: value.businessUnitId,
        code: value.code,
        captionI18n: value.captionI18n,
      });
      if (updated) {
        this.form.markAsPristine();
      }
    }
  }

  protected async deactivate(): Promise<void> {
    if (!this.warehouseId) {
      return;
    }
    await this.facade.deactivate(this.warehouseId);
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/warehouses');
  }
}
