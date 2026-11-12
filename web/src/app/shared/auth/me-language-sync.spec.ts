import { HttpContext } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { BehaviorSubject } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { Api } from '../../api/platform/api';
import { NX_SKIP_ERROR_NAVIGATION } from '../../core/http/skip-error-navigation';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';
import { MeLanguageSync } from './me-language-sync';

describe('MeLanguageSync', () => {
  function setup(invoke: () => Promise<unknown>) {
    const auth$ = new BehaviorSubject({ isAuthenticated: false });
    const setInitialLanguage = vi.fn();
    const api = { invoke: vi.fn((..._args: unknown[]) => invoke()) };
    TestBed.configureTestingModule({
      providers: [
        { provide: OidcSecurityService, useValue: { isAuthenticated$: auth$ } },
        { provide: Api, useValue: api },
        { provide: NxI18nService, useValue: { setInitialLanguage } },
      ],
    });
    return { sync: TestBed.inject(MeLanguageSync), auth$, setInitialLanguage, api };
  }

  it('loads /me once after login and applies preferredLanguage', async () => {
    const { sync, auth$, setInitialLanguage, api } = setup(async () => ({ preferredLanguage: 'fr' }));
    sync.start();
    sync.start();
    expect(api.invoke).not.toHaveBeenCalled();
    auth$.next({ isAuthenticated: true });
    await vi.waitFor(() => expect(setInitialLanguage).toHaveBeenCalledWith('fr'));
    expect(api.invoke).toHaveBeenCalledTimes(1);
    const context = api.invoke.mock.calls[0][2] as unknown as HttpContext;
    expect(context.get(NX_SKIP_ERROR_NAVIGATION)).toBe(true);
  });

  it('keeps the current language when /me fails', async () => {
    const { sync, auth$, setInitialLanguage, api } = setup(() => Promise.reject(new Error('401')));
    sync.start();
    auth$.next({ isAuthenticated: true });
    await vi.waitFor(() => expect(api.invoke).toHaveBeenCalled());
    await Promise.resolve();
    expect(setInitialLanguage).not.toHaveBeenCalled();
  });
});
