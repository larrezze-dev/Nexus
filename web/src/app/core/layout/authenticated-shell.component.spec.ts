import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { AuthenticatedShellComponent } from './authenticated-shell.component';
import { NavigationBuilderService } from './navigation-builder.service';
import { ShellComponent } from './shell.component';

describe('AuthenticatedShellComponent', () => {
  it('passes a TemplateRef rendering the language selector as ShellComponent.userMenuExtra', () => {
    TestBed.configureTestingModule({
      imports: [AuthenticatedShellComponent],
      providers: [
        provideRouter([]),
        { provide: NavigationBuilderService, useValue: { build: () => [] } },
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
      ],
    });

    const fixture = TestBed.createComponent(AuthenticatedShellComponent);
    fixture.detectChanges();

    const shell = fixture.debugElement.children[0].componentInstance as ShellComponent;
    expect(shell.userMenuExtra).toBeTruthy();

    // Render the projected template directly to confirm it is the language selector
    // (mat-menu only attaches its panel content to the DOM once opened).
    const view = shell.userMenuExtra!.createEmbeddedView(null);
    view.detectChanges();
    const host = document.createElement('div');
    view.rootNodes.forEach((node) => host.appendChild(node as Node));
    expect(host.querySelector('nx-language-selector')).toBeTruthy();
  });
});
