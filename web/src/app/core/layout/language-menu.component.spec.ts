import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NxI18nService } from '../i18n/nx-i18n.service';
import { LanguageMenuComponent } from './language-menu.component';

describe('LanguageMenuComponent', () => {
  let fixture: ComponentFixture<LanguageMenuComponent>;

  function setup(active: string) {
    const activeLang = signal(active);
    const setLanguage = vi.fn((code: string) => activeLang.set(code));
    TestBed.configureTestingModule({
      imports: [LanguageMenuComponent],
      providers: [
        provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
        { provide: NxI18nService, useValue: { activeLang, setLanguage } },
      ],
    });
    fixture = TestBed.createComponent(LanguageMenuComponent);
    fixture.detectChanges();
    return { setLanguage };
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="nx-shell-language-trigger"]');
  }

  function open(): void {
    trigger().click();
    fixture.detectChanges();
  }

  function item(code: string): HTMLButtonElement {
    return document.querySelector(`[data-testid="nx-shell-language-${code}"]`) as HTMLButtonElement;
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((node) => node.remove()));

  it('shows the active language in its own name on the button', () => {
    setup('de');
    expect(trigger().textContent).toContain('Deutsch');
  });

  it('lists the five languages in their own name, the active one checked', () => {
    setup('fr');
    open();
    const labels = ['de', 'en', 'fr', 'it', 'es'].map((code) => item(code)?.textContent?.trim());
    expect(labels).toEqual(['Deutsch', 'English', 'Français', 'Italiano', 'Español'].map((l) => expect.stringContaining(l)));
    expect(item('fr').getAttribute('aria-checked')).toBe('true');
    expect(item('fr').getAttribute('role')).toBe('menuitemradio');
    expect(item('de').getAttribute('aria-checked')).toBe('false');
    expect(item('es').getAttribute('lang')).toBe('es');
  });

  it('switches the language through NxI18nService and updates the button', () => {
    const { setLanguage } = setup('de');
    open();
    item('es').click();
    fixture.detectChanges();
    expect(setLanguage).toHaveBeenCalledWith('es');
    expect(trigger().textContent).toContain('Español');
  });

  it('does nothing when the active language is picked again', () => {
    const { setLanguage } = setup('de');
    open();
    item('de').click();
    expect(setLanguage).not.toHaveBeenCalled();
  });
});
