import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  MatDialogActions,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';

export const CONCURRENCY_DIALOG_RELOAD_RESULT = 'reload';

/**
 * "Otro usuario ha modificado este registro" dialog (ficha 0.5, track F point 2 / 409 `concurrency`).
 *
 * It is opened generically from `errorInterceptor`, which has no knowledge of which facade
 * or form triggered the conflicting request. The only action that is always correct from
 * that generic context is a full page reload, so "Recargar" reloads the whole page rather
 * than trying to re-invoke a specific facade's refresh method.
 */
@Component({
  selector: 'nx-concurrency-dialog',
  imports: [MatButtonModule, MatDialogTitle, MatDialogContent, MatDialogActions, TranslocoPipe],
  template: `
    <h2 matDialogTitle>{{ 'errors.concurrencyTitle' | transloco }}</h2>
    <mat-dialog-content>{{ 'errors.concurrencyMessage' | transloco }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="dialogRef.close()">
        {{ 'common.cancel' | transloco }}
      </button>
      <button mat-raised-button color="primary" type="button" (click)="reload()">
        {{ 'common.reload' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConcurrencyDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ConcurrencyDialogComponent>);

  reload(): void {
    this.dialogRef.close(CONCURRENCY_DIALOG_RELOAD_RESULT);
    window.location.reload();
  }
}
