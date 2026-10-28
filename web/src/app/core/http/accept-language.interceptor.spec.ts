import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { acceptLanguageInterceptor, ACCEPT_LANGUAGE_HEADER } from './accept-language.interceptor';

describe('acceptLanguageInterceptor', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([acceptLanguageInterceptor])),
        provideHttpClientTesting(),
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });
    return {
      http: TestBed.inject(HttpClient),
      controller: TestBed.inject(HttpTestingController),
      transloco: TestBed.inject(TranslocoService),
    };
  }

  it('adds Accept-Language with the currently active Transloco language', () => {
    const { http, controller, transloco } = setup();
    transloco.setActiveLang('fr');

    http.get('/v1/asset-types').subscribe();

    const req = controller.expectOne('/v1/asset-types');
    expect(req.request.headers.get(ACCEPT_LANGUAGE_HEADER)).toBe('fr');

    req.flush({});
    controller.verify();
  });

  it('reflects a language change made before the next request', () => {
    const { http, controller, transloco } = setup();
    transloco.setActiveLang('it');

    http.get('/v1/asset-types').subscribe();
    const first = controller.expectOne('/v1/asset-types');
    expect(first.request.headers.get(ACCEPT_LANGUAGE_HEADER)).toBe('it');
    first.flush({});

    transloco.setActiveLang('es');
    http.get('/v1/asset-types').subscribe();
    const second = controller.expectOne('/v1/asset-types');
    expect(second.request.headers.get(ACCEPT_LANGUAGE_HEADER)).toBe('es');
    second.flush({});

    controller.verify();
  });
});
