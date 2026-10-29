import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { LegalEntitiesFacade } from './legal-entities.facade';

/**
 * `LegalEntity` list (`conventions-frontend.md §4`). `GET /v1/legal-entities`
 * has no `q` text-search parameter in the contract (only `page`/`pageSize`/
 * `sort`), so unlike the generic "lista" template this page has no search
 * box — only server-side pagination and column sort via `nx-data-grid`.
 *
 * `nx-data-grid` only exposes a `(selectionChanged)` event, not a per-row
 * click/open event, so "open a row" is wired through single-row selection:
 * selecting exactly one row navigates straight to its detail page.
 */
@Component({
  selector: 'nx-legal-entity-list-page',
  standalone: true,
  imports: [RouterLink, TranslocoModule, NxCanDirective, NxDataGridComponent, MatButtonModule, MatIconModule],
  template: `
    <div class="nx-legal-entity-list-page">
      <header class="nx-legal-entity-list-page__toolbar">
        <h1>{{ 'org.legalEntities.title' | transloco }}</h1>
        <a
          mat-raised-button
          color="primary"
          routerLink="new"
          data-testid="le-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon>add</mat-icon>
          {{ 'org.legalEntities.new' | transloco }}
        </a>
      </header>

      <nx-data-grid
        [columns]="columns"
        [fetch]="fetch"
        [emptyMessageKey]="'org.legalEntities.empty'"
        (selectionChanged)="onSelectionChanged($event)"
      />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityListPage {
  protected readonly facade = inject(LegalEntitiesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  protected readonly columns: ColDef<LegalEntityListItem>[] = [
    { field: 'code', headerName: this.transloco.translate('org.legalEntities.fields.code'), sortable: true },
    {
      field: 'currencyCode',
      headerName: this.transloco.translate('org.legalEntities.fields.currencyCode'),
      sortable: true,
    },
    {
      field: 'countryCode',
      headerName: this.transloco.translate('org.legalEntities.fields.countryCode'),
      sortable: true,
    },
    {
      field: 'isActive',
      headerName: this.transloco.translate('org.legalEntities.fields.isActive'),
      cellRenderer: OrgActiveStatusCellComponent,
      sortable: true,
    },
  ];

  protected readonly fetch: NxDataGridFetchFn<LegalEntityListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort);

  protected onSelectionChanged(rows: LegalEntityListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate(['..', rows[0].id], { relativeTo: this.route });
    }
  }
}
