import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { ConcurrencyDialogService } from './concurrency-dialog.service';
import { NEXUS_UNAUTHORIZED_EVENT, errorInterceptor } from './error.interceptor';
import type { Problem } from './problem';

describe('errorInterceptor', () => {
  function setup() {
    const router = { navigate: vi.fn(), navigateByUrl: vi.fn() };
    const concurrencyDialog = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: ConcurrencyDialogService, useValue: concurrencyDialog },
      ],
    });
    return {
      http: TestBed.inject(HttpClient),
      controller: TestBed.inject(HttpTestingController),
      router,
      concurrencyDialog,
    };
  }

  it('passes 400 validation errors through unchanged, with per-field messages intact', () => {
    const { http, controller, router } = setup();
    let captured: HttpErrorResponse | undefined;

    http.post('/v1/asset-types', {}).subscribe({ error: (err) => (captured = err) });

    const problem: Problem = { type: 'validation', status: 400, errors: { code: ['required'] } };
    controller.expectOne('/v1/asset-types').flush(problem, { status: 400, statusText: 'Bad Request' });

    expect(captured?.status).toBe(400);
    expect((captured?.error as Problem).errors?.['code']).toEqual(['required']);
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('navigates to /login and dispatches NEXUS_UNAUTHORIZED_EVENT on 401', () => {
    const { http, controller, router } = setup();
    const listener = vi.fn();
    window.addEventListener(NEXUS_UNAUTHORIZED_EVENT, listener);

    http.get('/v1/asset-types').subscribe({ error: () => undefined });
    controller.expectOne('/v1/asset-types').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(listener).toHaveBeenCalledTimes(1);

    window.removeEventListener(NEXUS_UNAUTHORIZED_EVENT, listener);
  });

  it('navigates to /forbidden with the required permission as a query param on 403', () => {
    const { http, controller, router } = setup();

    http.get('/v1/asset-types').subscribe({ error: () => undefined });
    const problem: Problem = { type: 'forbidden', status: 403, requiredPermission: 'ASSET_TYPE:CREATE' };
    controller.expectOne('/v1/asset-types').flush(problem, { status: 403, statusText: 'Forbidden' });

    expect(router.navigate).toHaveBeenCalledWith(['/forbidden'], {
      queryParams: { permission: 'ASSET_TYPE:CREATE' },
    });
  });

  it('navigates to /not-found on 404', () => {
    const { http, controller, router } = setup();

    http.get('/v1/asset-types/missing').subscribe({ error: () => undefined });
    controller.expectOne('/v1/asset-types/missing').flush({}, { status: 404, statusText: 'Not Found' });

    expect(router.navigateByUrl).toHaveBeenCalledWith('/not-found');
  });

  it('opens the concurrency dialog on a 409 concurrency conflict, without navigating', () => {
    const { http, controller, router, concurrencyDialog } = setup();

    http.put('/v1/asset-types/1', {}).subscribe({ error: () => undefined });
    const problem: Problem = { type: 'concurrency', status: 409 };
    controller.expectOne('/v1/asset-types/1').flush(problem, { status: 409, statusText: 'Conflict' });

    expect(concurrencyDialog.open).toHaveBeenCalledTimes(1);
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('does not open the concurrency dialog for a non-concurrency 409 (idempotent-create conflict)', () => {
    const { http, controller, concurrencyDialog } = setup();
    let captured: HttpErrorResponse | undefined;

    http.post('/v1/asset-types', {}).subscribe({ error: (err) => (captured = err) });
    const problem: Problem = { type: 'idempotency_conflict', status: 409 };
    controller.expectOne('/v1/asset-types').flush(problem, { status: 409, statusText: 'Conflict' });

    expect(concurrencyDialog.open).not.toHaveBeenCalled();
    expect(captured?.status).toBe(409);
  });

  it('passes 422 business-rule errors through with the rule code intact', () => {
    const { http, controller, router } = setup();
    let captured: HttpErrorResponse | undefined;

    http.post('/v1/asset-types', {}).subscribe({ error: (err) => (captured = err) });
    const problem: Problem = { type: 'business_rule', status: 422, code: 'STK-R5' };
    controller.expectOne('/v1/asset-types').flush(problem, { status: 422, statusText: 'Unprocessable Entity' });

    expect((captured?.error as Problem).code).toBe('STK-R5');
    expect(router.navigate).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
