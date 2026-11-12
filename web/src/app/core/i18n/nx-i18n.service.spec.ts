import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { LANGUAGE_STORAGE_KEY, NX_LANGUAGE_PREFERENCE_SAVER, NxI18nService } from './nx-i18n.service';

describe('NxI18nService', () => {
  const originalLanguage = navigator.language;

  function stubBrowserLanguage(language: string): void {
    Object.defineProperty(navigator, 'language', { value: language, configurable: true });
  }

  function setup(extraProviders: unknown[] = []): NxI18nService {
    TestBed.configureTestingModule({
      providers: [
        ...(extraProviders as []),
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });
    return TestBed.inject(NxI18nService);
  }

  beforeEach(() => {
    localStorage.clear();
    document.documentElement.lang = '';
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
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

  it('nx.lang paints the first frame, then /me.preferredLanguage wins and is written to nx.lang', () => {
    stubBrowserLanguage('fr-FR');
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'it');
    const service = setup();
    expect(service.getActiveLanguage()).toBe('it');
    service.setInitialLanguage('es');
    expect(service.getActiveLanguage()).toBe('es');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('es');
  });

  it('setInitialLanguage never calls the saver', () => {
    const saver = vi.fn();
    const service = setup([{ provide: NX_LANGUAGE_PREFERENCE_SAVER, useValue: saver }]);
    service.setInitialLanguage('es');
    service.setInitialLanguage(null);
    service.setInitialLanguage('pt');
    expect(saver).not.toHaveBeenCalled();
  });

  it('a language picked in the selector before /me arrives is respected', () => {
    const service = setup();
    service.setLanguage('fr');
    service.setInitialLanguage('es');
    expect(service.getActiveLanguage()).toBe('fr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('fr');
  });

  it('an unusable preference keeps the stored language and does not overwrite nx.lang', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'it');
    const service = setup();
    service.setInitialLanguage('pt');
    expect(service.getActiveLanguage()).toBe('it');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('it');
  });

  it('clearStoredLanguage removes nx.lang and tolerates a throwing storage', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'it');
    const service = setup();
    service.clearStoredLanguage();
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBeNull();
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new Error('denied');
    });
    expect(() => service.clearStoredLanguage()).not.toThrow();
  });

  it('ignores an unsupported stored value and falls through to /me', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'pt');
    const service = setup();
    service.setInitialLanguage('es');
    expect(service.getActiveLanguage()).toBe('es');
  });

  it('resolves without throwing when localStorage access throws', () => {
    stubBrowserLanguage('fr-FR');
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    const service = setup();
    expect(service.getActiveLanguage()).toBe('fr');
    service.setInitialLanguage('es');
    expect(service.getActiveLanguage()).toBe('es');
    expect(() => service.setLanguage('en')).not.toThrow();
    expect(service.getActiveLanguage()).toBe('en');
  });

  it('setLanguage stores the choice and calls the preference saver', () => {
    const saver = vi.fn();
    const service = setup([{ provide: NX_LANGUAGE_PREFERENCE_SAVER, useValue: saver }]);
    service.setLanguage('es');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('es');
    expect(saver).toHaveBeenCalledWith('es');
  });

  it('a saver that throws synchronously never breaks the switch', () => {
    const service = setup([
      {
        provide: NX_LANGUAGE_PREFERENCE_SAVER,
        useValue: () => {
          throw new Error('boom');
        },
      },
    ]);
    expect(() => service.setLanguage('fr')).not.toThrow();
    expect(service.getActiveLanguage()).toBe('fr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('fr');
  });

  it('a failing (sync or async) preference saver never breaks the switch', async () => {
    const service = setup([
      {
        provide: NX_LANGUAGE_PREFERENCE_SAVER,
        useValue: () => Promise.reject(new Error('offline')),
      },
    ]);
    service.setLanguage('fr');
    await Promise.resolve();
    expect(service.getActiveLanguage()).toBe('fr');
  });

  it('keeps <html lang> equal to the active language on every change', () => {
    stubBrowserLanguage('fr-FR');
    const service = setup();
    expect(document.documentElement.lang).toBe('fr');
    service.setLanguage('es');
    expect(document.documentElement.lang).toBe('es');
    service.setInitialLanguage('de');
    expect(document.documentElement.lang).toBe('es'); // stored explicit choice wins
  });
});
