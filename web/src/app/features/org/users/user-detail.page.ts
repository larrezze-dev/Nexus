import { Component, OnInit, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormControl, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule } from '@jsverse/transloco';
import { LANGUAGE_OPTIONS } from '../../../core/i18n/supported-languages';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxEntityPickerComponent } from '../../../shared/ui/nx-entity-picker/nx-entity-picker.component';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { SYSTEM_USER_ID, UsersFacade } from './users.facade';

interface UserForm {
  oid: FormControl<string>;
  fullName: FormControl<string>;
  email: FormControl<string>;
  preferredLanguage: FormControl<'de' | 'en' | 'fr' | 'it' | 'es'>;
}

/**
 * User detail/create page (`*-detail.page`, `conventions-frontend.md §4`). A single "General"
 * tab: `User` has no `caption_i18n`, versions, documents or approvals to add further tabs for.
 *
 * ORG-R6 (SYSTEM never edited/deactivated): this page refuses to load `SYSTEM_USER_ID` for
 * editing and hides the deactivate action for it; the server rejects it too regardless.
 */
@Component({
  selector: 'nx-user-detail-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    NxCanDirective,
    NxEntityPickerComponent,
  ],
  templateUrl: './user-detail.page.html',
})
export class UserDetailPage implements OnInit {
  protected readonly facade = inject(UsersFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly languageOptions = LANGUAGE_OPTIONS;
  protected readonly isNew = signal(true);
  protected readonly isSystemUser = signal(false);
  protected readonly businessUnitId = signal<string | null>(null);
  protected readonly businessUnitLabel = signal<string | null>(null);

  protected readonly form = new FormGroup<UserForm>({
    oid: new FormControl('', { nonNullable: true }),
    fullName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
    preferredLanguage: new FormControl('de', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly searchBusinessUnits = this.facade.searchBusinessUnits;
  protected readonly displayBusinessUnit = (bu: BusinessUnitListItem): string => this.facade.formatBusinessUnit(bu);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      this.isNew.set(true);
      this.facade.reset();
      return;
    }

    this.isNew.set(false);
    this.isSystemUser.set(id === SYSTEM_USER_ID);
    await this.facade.load(id);

    const user = this.facade.selected();
    if (user) {
      this.form.setValue({
        oid: user.oid ?? '',
        fullName: user.fullName,
        email: user.email ?? '',
        preferredLanguage: user.preferredLanguage,
      });
      this.businessUnitId.set(user.businessUnitId ?? null);
      if (user.businessUnitId) {
        this.businessUnitLabel.set(await this.facade.describeBusinessUnit(user.businessUnitId));
      }
    }

    // ORG-R6: the SYSTEM user is never edited. Disable the whole form defensively on top of
    // hiding the save/deactivate actions in the template (the server rejects it either way).
    if (this.isSystemUser()) {
      this.form.disable();
    }
  }

  protected onBusinessUnitSelected(bu: BusinessUnitListItem): void {
    this.businessUnitId.set(bu.id);
    this.businessUnitLabel.set(this.facade.formatBusinessUnit(bu));
  }

  protected clearBusinessUnit(): void {
    this.businessUnitId.set(null);
    this.businessUnitLabel.set(null);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const businessUnitId = this.businessUnitId();

    if (this.isNew()) {
      const created = await this.facade.create({
        oid: value.oid,
        fullName: value.fullName,
        email: value.email || null,
        preferredLanguage: value.preferredLanguage,
        businessUnitId,
      });
      if (created) {
        void this.router.navigate(['..', created.id], { relativeTo: this.route });
      }
      return;
    }

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    await this.facade.update(id, {
      oid: value.oid || null,
      fullName: value.fullName,
      email: value.email || null,
      preferredLanguage: value.preferredLanguage,
      businessUnitId,
    });
  }

  protected async deactivate(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id || this.isSystemUser()) {
      return;
    }
    await this.facade.deactivate(id);
  }
}
