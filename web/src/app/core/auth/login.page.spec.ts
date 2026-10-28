import { TestBed } from '@angular/core/testing';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  it('starts the IdP authorization flow immediately (no local login form)', () => {
    const authorize = vi.fn();

    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [{ provide: OidcSecurityService, useValue: { authorize } }],
    });

    TestBed.createComponent(LoginPage);

    expect(authorize).toHaveBeenCalledOnce();
  });
});
