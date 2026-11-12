import { TestBed } from '@angular/core/testing';
import { NxConfirm } from './nx-confirm.service';
import { layoutTransloco } from './testing';

describe('NxConfirm', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [layoutTransloco()] }).compileComponents();
  });

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-container').forEach((container) => (container.innerHTML = ''));
  });

  async function open(destructive: boolean, confirmLabel?: string) {
    const result = TestBed.inject(NxConfirm).confirm({
      title: 'Deactivate warehouse?',
      message: 'Warehouse Barcelona 01 will no longer be selectable.',
      confirmLabel,
      destructive,
    });
    await vi.waitFor(() => expect(document.querySelector('nx-confirm-dialog')).toBeTruthy());
    TestBed.tick();
    const dialog = document.querySelector('nx-confirm-dialog') as HTMLElement;
    return { result, dialog };
  }

  it('shows title, message and the confirm label, and resolves true on confirm', async () => {
    const { result, dialog } = await open(true, 'Deactivate');

    expect(dialog.querySelector('h2')?.textContent?.trim()).toBe('Deactivate warehouse?');
    expect(dialog.textContent).toContain('Warehouse Barcelona 01 will no longer be selectable.');
    const ok = dialog.querySelector('[data-testid="nx-confirm-ok"]') as HTMLButtonElement;
    expect(ok.textContent?.trim()).toBe('Deactivate');
    expect(ok.classList).toContain('nx-btn--danger');
    expect(document.querySelector('mat-dialog-container, .mat-mdc-dialog-container')?.getAttribute('role')).toBe('alertdialog');

    ok.click();
    await expect(result).resolves.toBe(true);
  });

  it('resolves false on Cancel, uses the default label and no danger style for a non-destructive action', async () => {
    const { result, dialog } = await open(false);

    const ok = dialog.querySelector('[data-testid="nx-confirm-ok"]') as HTMLButtonElement;
    expect(ok.textContent?.trim()).toBe('Confirm');
    expect(ok.classList).not.toContain('nx-btn--danger');

    const cancel = dialog.querySelector('[data-testid="nx-confirm-cancel"]') as HTMLButtonElement;
    expect(cancel.textContent?.trim()).toBe('Cancel');
    cancel.click();
    await expect(result).resolves.toBe(false);
  });
});
