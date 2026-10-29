import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoModule } from '@jsverse/transloco';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { PartiesFacade } from './parties.facade';

interface PartyForm {
  partyType: FormControl<'ORGANIZATION' | 'PERSON'>;
  legalName: FormControl<string>;
  displayName: FormControl<string>;
  taxId: FormControl<string>;
  erpCompanyCode: FormControl<string>;
  isInternal: FormControl<boolean>;
}

/**
 * Party detail/create page (`*-detail.page`, `conventions-frontend.md §4`). A single "General"
 * tab: `Party` has no `caption_i18n`, versions, documents or approvals in this contract.
 *
 * `partyType` and `isInternal` are immutable after creation (ORG-R2, per `UpdatePartyRequest`'s
 * doc comment): both are disabled once editing an existing party.
 *
 * Manual alta from this directory is expected to create EXTERNAL parties (`isInternal: false`):
 * internal legal entities/business units/warehouses are created from their own structure
 * screens, not here. This page does not force the value, only defaults the create form to
 * `false` and leaves the checkbox editable, per the subtask brief.
 */
@Component({
  selector: 'nx-party-detail-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    NxCanDirective,
  ],
  templateUrl: './party-detail.page.html',
})
export class PartyDetailPage implements OnInit {
  protected readonly facade = inject(PartiesFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly isNew = signal(true);

  protected readonly form = new FormGroup<PartyForm>({
    partyType: new FormControl('ORGANIZATION', { nonNullable: true, validators: [Validators.required] }),
    legalName: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    displayName: new FormControl('', { nonNullable: true }),
    taxId: new FormControl('', { nonNullable: true }),
    erpCompanyCode: new FormControl('', { nonNullable: true }),
    isInternal: new FormControl(false, { nonNullable: true }),
  });

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      this.isNew.set(true);
      this.facade.reset();
      return;
    }

    this.isNew.set(false);
    await this.facade.load(id);

    const party = this.facade.selected();
    if (party) {
      this.form.setValue({
        partyType: party.partyType,
        legalName: party.legalName,
        displayName: party.displayName ?? '',
        taxId: party.taxId ?? '',
        erpCompanyCode: party.erpCompanyCode ?? '',
        isInternal: party.isInternal,
      });
      // ORG-R2: `partyType`/`isInternal` are immutable after creation; `UpdatePartyRequest`
      // does not even accept them.
      this.form.controls.partyType.disable();
      this.form.controls.isInternal.disable();
    }
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    if (this.isNew()) {
      const created = await this.facade.create({
        partyType: value.partyType,
        legalName: value.legalName,
        displayName: value.displayName || null,
        taxId: value.taxId || null,
        erpCompanyCode: value.erpCompanyCode || null,
        isInternal: value.isInternal,
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
      legalName: value.legalName,
      displayName: value.displayName || null,
      taxId: value.taxId || null,
      erpCompanyCode: value.erpCompanyCode || null,
    });
  }

  protected async deactivate(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    await this.facade.deactivate(id);
  }
}
