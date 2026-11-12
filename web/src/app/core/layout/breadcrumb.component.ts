import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NxBreadcrumbService } from './breadcrumb.service';

/**
 * Breadcrumb trail "section group › list › current page", driven by the leaf route's `data`
 * and the record name published through `NxBreadcrumbService` (see its doc for the contract).
 * Only list crumbs link; the group and the current page are plain text.
 */
@Component({
  selector: 'app-breadcrumb',
  standalone: true,
  imports: [RouterLink, TranslocoPipe],
  templateUrl: './breadcrumb.component.html',
  styleUrl: './breadcrumb.component.scss',
})
export class BreadcrumbComponent {
  protected readonly crumbs = inject(NxBreadcrumbService).trail;
}
