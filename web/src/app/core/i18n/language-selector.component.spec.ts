import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { LanguageSelectorComponent } from './language-selector.component';
import { NxI18nService } from './nx-i18n.service';

describe('LanguageSelectorComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });
    // Forces NxI18nService's constructor (which sets the active language from the browser)
    // to run now, like provideNexusI18n()'s provideAppInitializer would at bootstrap, so a
    // later explicit transloco.setActiveLang() in the test isn't overwritten by it.
    TestBed.inject(NxI18nService);
    return TestBed.inject(TranslocoService);
  }

  it('shows the native label of the currently active language', () => {
    const transloco = setup();
    transloco.setActiveLang('fr');

    const fixture = TestBed.createComponent(LanguageSelectorComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Français');
  });

  it('switching the active language through NxI18nService/TranslocoService updates the shown label', async () => {
    const transloco = setup();
    transloco.setActiveLang('de');

    const fixture = TestBed.createComponent(LanguageSelectorComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Deutsch');

    transloco.setActiveLang('es');
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Español');
  });

  it('selecting a language from the menu calls through to TranslocoService.setActiveLang', () => {
    const transloco = setup();
    transloco.setActiveLang('de');

    const fixture = TestBed.createComponent(LanguageSelectorComponent);
    fixture.detectChanges();

    fixture.componentInstance.select('it');

    expect(transloco.getActiveLang()).toBe('it');
  });
});
