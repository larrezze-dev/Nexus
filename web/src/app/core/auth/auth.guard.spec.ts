import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom, of, type Observable } from 'rxjs';
import { authGuard } from './auth.guard';
import { consumeReturnUrl } from './return-url';

describe('authGuard', () => {
  function runGuard(
    oidcSecurityService: Partial<OidcSecurityService>,
    url = '/',
  ): Observable<boolean> {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [{ provide: OidcSecurityService, useValue: oidcSecurityService }],
    });

    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    ) as Observable<boolean>;
  }

  it('lets navigation through when there is a session', async () => {
    const authorize = vi.fn();

    const result = await firstValueFrom(
      runGuard({
        isAuthenticated$: of({ isAuthenticated: true, allConfigsAuthenticated: [] }),
        authorize,
      }),
    );

    expect(result).toBe(true);
    expect(authorize).not.toHaveBeenCalled();
  });

  it('redirects to the IdP (does not let navigation through) when there is no session', async () => {
    const authorize = vi.fn();

    const result = await firstValueFrom(
      runGuard({
        isAuthenticated$: of({ isAuthenticated: false, allConfigsAuthenticated: [] }),
        authorize,
      }),
    );

    expect(result).toBe(false);
    expect(authorize).toHaveBeenCalledOnce();
  });

  it('remembers the deep link before redirecting to the IdP', async () => {
    const authorize = vi.fn();

    await firstValueFrom(
      runGuard(
        {
          isAuthenticated$: of({ isAuthenticated: false, allConfigsAuthenticated: [] }),
          authorize,
        },
        '/warehouses',
      ),
    );

    expect(consumeReturnUrl()).toBe('/warehouses');
  });
});
