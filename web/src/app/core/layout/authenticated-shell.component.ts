import { Component } from '@angular/core';
import { ShellComponent } from './shell.component';

/**
 * Parent component of every authenticated route in `app.routes.ts`. Today it only hosts
 * `ShellComponent` (which now carries the language selector and user menu in its top bar); it is
 * kept as the routing seam so later steps can add authenticated-only chrome without touching
 * the route configuration.
 */
@Component({
  selector: 'nx-authenticated-shell',
  imports: [ShellComponent],
  template: `<app-shell />`,
})
export class AuthenticatedShellComponent {}
