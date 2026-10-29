import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoModule } from '@jsverse/transloco';
import { LocationFacade } from './location.facade';

/**
 * Location create/edit screen (ficha 0.4, track F): Reactive Form, `If-Match` on save
 * (via `LocationFacade`), no deactivation action (the 0.4 contract has none for Location).
 * The same component handles both `/locations/new` and `/locations/:id`.
 */
@Component({
  selector: 'nx-location-detail-page',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslocoModule],
  template: `
    <h1>{{ (isNew() ? 'locations.detail.title.create' : 'locations.detail.title.edit') | transloco }}</h1>

    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-form-field>
        <mat-label>{{ 'locations.fields.code' | transloco }}</mat-label>
        <input matInput formControlName="code" data-testid="location-code-input" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.name' | transloco }}</mat-label>
        <input matInput formControlName="name" data-testid="location-name-input" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.street' | transloco }}</mat-label>
        <input matInput formControlName="street" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.postalCode' | transloco }}</mat-label>
        <input matInput formControlName="postalCode" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.city' | transloco }}</mat-label>
        <input matInput formControlName="city" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.region' | transloco }}</mat-label>
        <input matInput formControlName="region" />
      </mat-form-field>

      <mat-form-field>
        <mat-label>{{ 'locations.fields.countryCode' | transloco }}</mat-label>
        <input matInput formControlName="countryCode" maxlength="2" data-testid="location-country-input" />
      </mat-form-field>

      @if (facade.error(); as error) {
        <p role="alert" class="nx-form-error" data-testid="location-form-error">{{ error }}</p>
      }

      <div class="nx-form-actions">
        <button mat-button type="button" (click)="cancel()">{{ 'common.cancel' | transloco }}</button>
        <button mat-raised-button color="primary" type="submit" [disabled]="facade.loading()" data-testid="location-save-button">
          {{ 'common.save' | transloco }}
        </button>
      </div>
    </form>
  `,
})
export class LocationDetailPage implements OnInit {
  protected readonly facade = inject(LocationFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly isNew = signal(true);
  private locationId: string | null = null;

  protected readonly form = new FormGroup({
    code: new FormControl<string | null>(null),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    street: new FormControl<string | null>(null),
    postalCode: new FormControl<string | null>(null),
    city: new FormControl<string | null>(null),
    region: new FormControl<string | null>(null),
    countryCode: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^[A-Z]{2}$/)],
    }),
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id && id !== 'new') {
      this.isNew.set(false);
      this.locationId = id;
      void this.loadExisting(id);
    } else {
      this.facade.reset();
    }
  }

  private async loadExisting(id: string): Promise<void> {
    await this.facade.load(id);
    const location = this.facade.selected();
    if (location) {
      this.form.patchValue(location);
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    if (this.isNew()) {
      const created = await this.facade.create(value);
      if (created) {
        void this.router.navigateByUrl(`/locations/${created.id}`);
      }
    } else if (this.locationId) {
      const updated = await this.facade.update(this.locationId, value);
      if (updated) {
        this.form.markAsPristine();
      }
    }
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/locations');
  }
}
