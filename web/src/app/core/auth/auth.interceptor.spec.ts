import { TestBed } from '@angular/core/testing';
import { HttpRequest, type HttpHandlerFn } from '@angular/common/http';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom, of, type Observable } from 'rxjs';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  function run(req: HttpRequest<unknown>, getAccessToken: () => Observable<string>) {
    TestBed.configureTestingModule({
      providers: [{ provide: OidcSecurityService, useValue: { getAccessToken } }],
    });

    const next = vi.fn((forwardedReq: HttpRequest<unknown>) => of(forwardedReq)) as unknown as (
      req: HttpRequest<unknown>,
    ) => ReturnType<HttpHandlerFn>;

    const result$ = TestBed.runInInjectionContext(() =>
      authInterceptor(req, next as HttpHandlerFn),
    );

    return { result$, next };
  }

  it('adds Authorization: Bearer <token> to requests under /api/', async () => {
    const getAccessToken = vi.fn(() => of('token-123'));
    const req = new HttpRequest('GET', '/api/platform/v1/me');

    const { result$, next } = run(req, getAccessToken);
    const forwarded = (await firstValueFrom(result$)) as unknown as HttpRequest<unknown>;

    expect(getAccessToken).toHaveBeenCalledOnce();
    expect(next).toHaveBeenCalledOnce();
    expect(forwarded.headers.get('Authorization')).toBe('Bearer token-123');
  });

  it('does not touch requests outside /api/', async () => {
    const getAccessToken = vi.fn(() => of('token-123'));
    const req = new HttpRequest('GET', '/assets/i18n/en.json');

    const { result$, next } = run(req, getAccessToken);
    const forwarded = (await firstValueFrom(result$)) as unknown as HttpRequest<unknown>;

    expect(getAccessToken).not.toHaveBeenCalled();
    expect(next).toHaveBeenCalledOnce();
    expect(forwarded.headers.has('Authorization')).toBe(false);
  });
});
