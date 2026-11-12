import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';
import { AuthenticatedShellComponent } from './authenticated-shell.component';
import { NavigationBuilderService } from './navigation-builder.service';

describe('AuthenticatedShellComponent', () => {
  it('hosts the application shell with the language selector and user menu in the top bar', () => {
    TestBed.configureTestingModule({
      imports: [AuthenticatedShellComponent],
      providers: [
        provideRouter([]),
        { provide: NavigationBuilderService, useValue: { buildGrouped: () => [] } },
        { provide: OidcSecurityService, useValue: { userData$: of({ userData: null }), logoff: () => of(null) } },
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });

    const fixture = TestBed.createComponent(AuthenticatedShellComponent);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('app-shell')).toBeTruthy();
    expect(element.querySelector('[data-testid="nx-shell-language-trigger"]')).toBeTruthy();
    expect(element.querySelector('[data-testid="nx-shell-user-menu-trigger"]')).toBeTruthy();
  });
});
