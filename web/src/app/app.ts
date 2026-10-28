import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * Application root. The real shell/navigation lives in the route table
 * (`app.routes.ts`, `AuthenticatedShellComponent`/`ShellComponent`) — this
 * is just the router's attachment point.
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {}
