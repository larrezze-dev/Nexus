import { Injectable, inject } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { ConcurrencyDialogComponent } from './concurrency-dialog.component';

/**
 * Opens the "Otro usuario ha modificado este registro" dialog on a 409 `concurrency` conflict.
 * `errorInterceptor` calls `open()`; it is also safe for a form/facade to call directly if it
 * detects the same conflict outside an HTTP error (e.g. from a `selectTranslate`-driven retry).
 */
@Injectable({ providedIn: 'root' })
export class ConcurrencyDialogService {
  private readonly dialog = inject(MatDialog);
  private openDialogRef: MatDialogRef<ConcurrencyDialogComponent> | null = null;

  open(): MatDialogRef<ConcurrencyDialogComponent> {
    if (this.openDialogRef) {
      return this.openDialogRef;
    }
    const ref = this.dialog.open(ConcurrencyDialogComponent, { width: '420px' });
    this.openDialogRef = ref;
    ref.afterClosed().subscribe(() => {
      if (this.openDialogRef === ref) {
        this.openDialogRef = null;
      }
    });
    return ref;
  }
}
