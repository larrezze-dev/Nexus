import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { afterEach, describe, expect, it } from 'vitest';
import { NxI18nService } from './nx-i18n.service';

describe('NxI18nService', () => {
  const originalLanguage = navigator.language;

  function stubBrowserLanguage(language: string): void {
    Object.defineProperty(navigator, 'language', { value: language, configurable: true });
  }

  function setup(): NxI18nService {
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });
    return TestBed.inject(NxI18nService);
  }

  afterEach(() => {
    Object.defineProperty(navigator, 'language', { value: originalLanguage, configurable: true });
  });

  it('uses the supported browser language at construction when no preferred language is known yet', () => {
    stubBrowserLanguage('fr-FR');
    const service = setup();
    expect(service.getActiveLanguage()).toBe('fr');
  });

  it('falls back to de at construction when the browser language is not supported', () => {
    stubBrowserLanguage('zh-CN');
    const service = setup();
    expect(service.getActiveLanguage()).toBe('de');
  });

  it('setInitialLanguage prefers a supported preferredLanguage over the browser language', () => {
    stubBrowserLanguage('fr-FR');
    const service = setup();
    service.setInitialLanguage('es');
    expect(service.getActiveLanguage()).toBe('es');
  });

  it('setInitialLanguage falls back to the supported browser language when preferredLanguage is null', () => {
    stubBrowserLanguage('fr-FR');
    const service = setup();
    service.setInitialLanguage(null);
    expect(service.getActiveLanguage()).toBe('fr');
  });

  it('setInitialLanguage falls back to de when neither preferredLanguage nor the browser language are supported', () => {
    stubBrowserLanguage('zh-CN');
    const service = setup();
    service.setInitialLanguage('pt');
    expect(service.getActiveLanguage()).toBe('de');
  });

  it('translateErrorCode returns the specific errors.<code> translation when it exists', () => {
    const service = setup();
    const transloco = TestBed.inject(TranslocoService);
    // Translations are loaded asynchronously by the (http) loader in a real app; here the
    // instant setTranslation keeps the test synchronous and loader-independent.
    transloco.setActiveLang('de');
    transloco.setTranslation({ errors: { 'SAMPLE-R1': 'Specific message' } }, 'de');
    expect(service.translateErrorCode('SAMPLE-R1')).toBe('Specific message');
  });

  it('translateErrorCode falls back to errors.generic when the code has no specific translation', () => {
    const service = setup();
    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('de');
    transloco.setTranslation({ errors: { generic: 'Generic fallback' } }, 'de');
    expect(service.translateErrorCode('UNKNOWN-R9')).toBe('Generic fallback');
  });
});
