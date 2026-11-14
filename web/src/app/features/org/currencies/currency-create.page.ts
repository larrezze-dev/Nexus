import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import {
  isDuplicateConflict,
  localizedFieldErrors,
  problemOf,
} from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import { CurrenciesFacade } from './currencies.facade';
import { CurrencyFormComponent, CurrencyFormValue } from './currency-form.component';

/**
 * New currency (`POST /v1/currencies`, permission `CURRENCY:CREATE`): the create form, then the
 * detail page of the new currency. A 409 `CONFLICT` means the ISO code already exists and is
 * shown on the `code` field; a 400 marks the fields with a generic «Invalid value».
 */
@Component({
  selector: 'nx-currency-create-page',
  standalone: true,
  imports: [
    TranslocoModule,
    MatIconModule,
    NxPageComponent,
    NxPageHeaderComponent,
    CurrencyFormComponent,
  ],
  template: `
    <nx-page width="form">
      <nx-page-header
        [breadcrumb]="true"
        [title]="'org.currencies.newTitle' | transloco"
        [subtitle]="'org.currencies.createSubtitle' | transloco"
      />
      @if (saveError()) {
        <div class="nx-alert nx-alert--error" role="alert" data-testid="currency-create-error">
          <mat-icon aria-hidden="true">error</mat-icon>
          <span class="nx-alert__text">{{ saveError() }}</span>
        </div>
      }
      <nx-currency-form
        mode="create"
        [saving]="saving()"
        [fieldErrors]="fieldErrors()"
        (save)="onSave($event)"
        (cancel)="onCancel()"
      />
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrencyCreatePage {
  private readonly facade = inject(CurrenciesFacade);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly i18n = inject(NxI18nService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly fieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected async onSave(value: CurrencyFormValue): Promise<void> {
    this.saving.set(true);
    this.saveError.set(null);
    this.fieldErrors.set({});
    try {
      const created = await this.facade.create({
        code: value.code,
        captionI18n: value.captionI18n,
        minorUnits: value.minorUnits,
      });
      this.snackBar.open(this.transloco.translate('org.currencies.created'), undefined, {
        duration: 4000,
      });
      void this.router.navigate(['/org/currencies', created.code]);
    } catch (error: unknown) {
      this.handleSaveError(error);
    } finally {
      this.saving.set(false);
    }
  }

  protected onCancel(): void {
    void this.router.navigateByUrl('/org/currencies');
  }

  private handleSaveError(error: unknown): void {
    const http = error as { status?: number } | undefined;
    const problem = problemOf(error);
    if (isDuplicateConflict(error)) {
      this.fieldErrors.set({ code: [this.transloco.translate('org.currencies.errors.duplicateCode')] });
      return;
    }
    if (http?.status === 400 && problem?.errors) {
      this.fieldErrors.set(
        localizedFieldErrors(problem.errors, this.transloco.translate('shared.form.invalidValue')),
      );
      this.saveError.set(this.transloco.translate('errors.validation'));
      return;
    }
    this.saveError.set(this.i18n.translateErrorCode(http?.status === 422 ? problem?.code : undefined));
  }
}
