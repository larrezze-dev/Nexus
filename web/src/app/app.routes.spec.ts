import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTransloco } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { BehaviorSubject } from 'rxjs';
import { routes } from './app.routes';
import { nexusHttpRoutes } from './core/http/http.routes';

describe('app routes (error pages)', () => {
  const authenticated$ = new BehaviorSubject({ isAuthenticated: true });
  const authorize = vi.fn();

  beforeEach(() => {
    authorize.mockReset();
    authenticated$.next({ isAuthenticated: true });
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        {
          provide: OidcSecurityService,
          useValue: {
            isAuthenticated$: authenticated$,
            authorize,
            userData$: new BehaviorSubject({ userData: null }),
          },
        },
      ],
    });
  });

  it('an unknown URL renders the not-found page INSIDE the shell', async () => {
    const harness = await RouterTestingHarness.create('/does/not/exist');

    const root: HTMLElement = harness.routeNativeElement!.ownerDocument.body;
    expect(root.querySelector('[data-testid="nx-not-found-page"]')).not.toBeNull();
    expect(root.querySelector('.nx-shell')).not.toBeNull();
  });

  it('/forbidden and /not-found live inside the shell', async () => {
    const harness = await RouterTestingHarness.create('/forbidden');
    const body = harness.routeNativeElement!.ownerDocument.body;
    expect(body.querySelector('.nx-shell')).not.toBeNull();
    expect(body.querySelector('[data-testid="nx-forbidden-page"]')).not.toBeNull();

    await harness.navigateByUrl('/not-found');
    expect(body.querySelector('.nx-shell')).not.toBeNull();
    expect(body.querySelector('[data-testid="nx-not-found-page"]')).not.toBeNull();
  });

  it('the wildcard is the last child of the shell', () => {
    expect(nexusHttpRoutes.at(-1)?.path).toBe('**');
  });

  it('/unauthorized is reachable without authentication, outside the shell, and signs in again', async () => {
    authenticated$.next({ isAuthenticated: false });

    const harness = await RouterTestingHarness.create('/unauthorized');

    const body = harness.routeNativeElement!.ownerDocument.body;
    expect(body.querySelector('[data-testid="nx-unauthorized-page"]')).not.toBeNull();
    expect(body.querySelector('.nx-shell')).toBeNull();
    expect(authorize).not.toHaveBeenCalled();

    (body.querySelector('[data-testid="nx-unauthorized-sign-in"]') as HTMLButtonElement).click();
    expect(authorize).toHaveBeenCalledOnce();
  });
});
