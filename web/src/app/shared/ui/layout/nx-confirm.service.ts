import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

/** Texts of a confirmation dialog, already translated. */
export interface NxConfirmOptions {
  readonly title: string;
  /** Sentence naming the record and the consequence, e.g. «Deactivate “Warehouse Barcelona 01”? …». */
  readonly message: string;
  /** Label of the confirm button; defaults to «Confirm». */
  readonly confirmLabel?: string;
  /** Destructive action (deactivate, delete): the confirm button is red. */
  readonly destructive?: boolean;
}

/** Dialog data: the options plus the id of the message element (target of `aria-describedby`). */
interface NxConfirmDialogData extends NxConfirmOptions {
  readonly messageId: string;
}

let nextConfirmId = 0;

/** Dialog body used by `NxConfirm`. Not meant to be opened directly. */
@Component({
  selector: 'nx-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p class="nx-confirm-dialog__message" [id]="data.messageId">{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="false" data-testid="nx-confirm-cancel">
        {{ 'common.cancel' | transloco }}
      </button>
      <button
        mat-flat-button
        type="button"
        [class.nx-btn--danger]="data.destructive"
        [mat-dialog-close]="true"
        data-testid="nx-confirm-ok"
      >
        {{ data.confirmLabel || ('shared.layout.confirm.confirm' | transloco) }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .nx-confirm-dialog__message {
      margin: 0;
      font: var(--nx-text-body);
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxConfirmDialogComponent {
  protected readonly data = inject<NxConfirmDialogData>(MAT_DIALOG_DATA);
}

/**
 * Confirmation dialog (`conventions-frontend.md §4.1`): never the browser's `confirm()`.
 * Resolves `true` only when the user presses the confirm button; Cancel, Esc and a click
 * outside resolve `false`.
 *
 * ```ts
 * if (await this.confirm.confirm({ title, message, confirmLabel, destructive: true })) { … }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class NxConfirm {
  private readonly dialog = inject(MatDialog);

  async confirm(options: NxConfirmOptions): Promise<boolean> {
    const messageId = `nx-confirm-message-${nextConfirmId++}`;
    const ref = this.dialog.open<NxConfirmDialogComponent, NxConfirmDialogData, boolean>(
      NxConfirmDialogComponent,
      {
        data: { ...options, messageId },
        ariaDescribedBy: messageId,
        width: '480px',
        maxWidth: 'calc(100vw - 32px)',
        // Focus starts on Cancel (first button): Enter never confirms a destructive action by accident.
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        role: options.destructive ? 'alertdialog' : 'dialog',
      },
    );
    return (await firstValueFrom(ref.afterClosed())) === true;
  }
}
