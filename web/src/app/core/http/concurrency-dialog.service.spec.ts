import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { ConcurrencyDialogService } from './concurrency-dialog.service';
import { CONCURRENCY_DIALOG_RELOAD_RESULT } from './concurrency-dialog.component';

describe('ConcurrencyDialogService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        ...provideTransloco({
          config: { availableLangs: ['de', 'en'], defaultLang: 'de' },
        }),
      ],
    });
  });

  it('opens a real MatDialog showing the concurrency message and a reload button that closes the dialog with the reload result', async () => {
    const service = TestBed.inject(ConcurrencyDialogService);

    const ref = service.open();
    // CDK overlay attaches the dialog to the DOM asynchronously; wait for the app to settle.
    await TestBed.inject(ApplicationRef).whenStable();

    const overlay = document.body;
    expect(overlay.textContent).toContain('errors.concurrencyMessage');

    const buttons = Array.from(overlay.querySelectorAll('button'));
    const reloadButton = buttons.find((button) => button.textContent?.includes('common.reload'));
    expect(reloadButton).toBeTruthy();

    let closedWith: unknown;
    ref.afterClosed().subscribe((result) => (closedWith = result));
    // `reload()` itself calls `window.location.reload()`, which jsdom does not implement
    // (it logs "Not implemented: navigation", harmlessly); this test only asserts the
    // dialog's own contract (closes with the reload result).
    reloadButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await TestBed.inject(ApplicationRef).whenStable();

    expect(closedWith).toBe(CONCURRENCY_DIALOG_RELOAD_RESULT);
  });

  it('does not open a second dialog while one is already open', () => {
    const service = TestBed.inject(ConcurrencyDialogService);
    const first = service.open();
    const second = service.open();
    expect(second).toBe(first);
  });
});
