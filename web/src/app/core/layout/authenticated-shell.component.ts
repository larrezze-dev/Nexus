import { Component } from '@angular/core';
import { LanguageSelectorComponent } from '../i18n/language-selector.component';
import { ShellComponent } from './shell.component';

/**
 * Wires `core/i18n`'s `LanguageSelectorComponent` into `ShellComponent.userMenuExtra`, exactly
 * as documented on `ShellComponent`'s class comment. `app.routes.ts` uses this (not
 * `ShellComponent` directly) as the parent/authenticated route, because `userMenuExtra` is a
 * `TemplateRef` input and the router cannot bind a `TemplateRef` from route config.
 */
@Component({
  selector: 'nx-authenticated-shell',
  imports: [ShellComponent, LanguageSelectorComponent],
  template: `
    <app-shell [userMenuExtra]="extra">
      <ng-template #extra><nx-language-selector /></ng-template>
    </app-shell>
  `,
})
export class AuthenticatedShellComponent {}
