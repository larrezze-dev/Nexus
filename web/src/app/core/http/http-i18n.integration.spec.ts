import { Component, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { describe, expect, it } from 'vitest';
import { nexusHttpRoutes } from './http.routes';
import { provideNexusHttp } from './provide-nexus-http';
import { provideNexusI18n } from '../i18n/provide-nexus-i18n';
import { ForbiddenPage } from './pages/forbidden.page';

/**
 * Minimal stand-in for the page that issues the failing request, wired through the real
 * `provideNexusHttp()`/`provideNexusI18n()` providers end to end (real HttpClient pipeline,
 * real Router, real routes this track owns) rather than mocking each interceptor in isolation.
 */
@Component({ selector: 'nx-test-caller', template: '' })
class CallerPage {
  private readonly http = inject(HttpClient);

  trigger(): void {
    this.http.get('/v1/asset-types').subscribe({ error: () => undefined });
  }
}

describe('provideNexusHttp() + provideNexusI18n() integration', () => {
  it('a 403 Problem from a real request navigates through the real router to the real ForbiddenPage with the required permission', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideNexusHttp(),
        provideNexusI18n(),
        provideHttpClientTesting(),
        provideRouter([
          { path: '', component: CallerPage },
          ...nexusHttpRoutes,
          { path: 'login', component: CallerPage },
        ]),
      ],
    });

    const harness = await RouterTestingHarness.create('/');
    const caller = harness.routeDebugElement?.componentInstance as CallerPage;
    caller.trigger();

    const controller = TestBed.inject(HttpTestingController);
    const req = controller.expectOne('/v1/asset-types');
    req.flush(
      { type: 'forbidden', status: 403, requiredPermission: 'ASSET_TYPE:CREATE' },
      { status: 403, statusText: 'Forbidden' },
    );

    const router = TestBed.inject(Router);
    // The interceptor's router.navigate() call is async; Angular's Router registers a
    // pending task while a navigation is in flight, so whenStable() waits for it to settle.
    await harness.fixture.whenStable();

    expect(router.url.startsWith('/forbidden')).toBe(true);

    const forbiddenComponent = harness.routeDebugElement?.componentInstance as ForbiddenPage;
    expect(forbiddenComponent).toBeInstanceOf(ForbiddenPage);

    // ForbiddenPage's `transloco` pipe lazily fetches the active language's translation file
    // (real NxTranslocoHttpLoader, intercepted by HttpClientTesting). Flush it with just the
    // key this test needs, then let the pipe re-render with the real translated text.
    const translationRequest = controller.expectOne((r) => r.url.includes('/assets/i18n/'));
    translationRequest.flush({
      httpPages: { forbidden: { requiredPermission: 'Required permission: {{permission}}' } },
    });
    await harness.fixture.whenStable();
    harness.detectChanges();

    const text = harness.routeNativeElement?.textContent ?? '';
    expect(text).toContain('ASSET_TYPE:CREATE');

    controller.verify();
  });
});
