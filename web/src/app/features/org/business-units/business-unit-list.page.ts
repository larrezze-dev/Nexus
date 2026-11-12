import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { BusinessUnitsFacade } from './business-units.facade';

/**
 * `BusinessUnit` list — same pattern as `LegalEntityListPage`: no `q`
 * text-search box (the contract's `GET /v1/business-units` only takes
 * `page`/`pageSize`/`sort`), single-row selection navigates to the detail
 * page.
 */
@Component({
  selector: 'nx-business-unit-list-page',
  standalone: true,
  imports: [RouterLink, TranslocoModule, NxCanDirective, NxDataGridComponent, MatButtonModule, MatIconModule],
  template: `
    <div class="nx-business-unit-list-page">
      <header class="nx-business-unit-list-page__toolbar">
        <h1>{{ 'org.businessUnits.title' | transloco }}</h1>
        <a
          mat-raised-button
          color="primary"
          routerLink="new"
          data-testid="bu-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon>add</mat-icon>
          {{ 'org.businessUnits.new' | transloco }}
        </a>
      </header>

      <nx-data-grid
        [columns]="columns()"
        [fetch]="fetch"
        [emptyMessageKey]="'org.businessUnits.empty'"
        (selectionChanged)="onSelectionChanged($event)"
      />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitListPage {
  protected readonly facade = inject(BusinessUnitsFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);

  private readonly i18n = inject(NxI18nService);

  /** Computed so headers and captions follow the active language. */
  protected readonly columns = computed<ColDef<BusinessUnitListItem>[]>(() => {
    const language = this.i18n.activeLang();
    return [
      { field: 'code', headerName: this.transloco.translate('org.businessUnits.fields.code'), sortable: true },
      {
        headerName: this.transloco.translate('org.businessUnits.fields.caption'),
        valueGetter: (params) => resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
        sortable: false,
      },
      {
        field: 'isActive',
        headerName: this.transloco.translate('org.businessUnits.fields.isActive'),
        cellRenderer: OrgActiveStatusCellComponent,
        sortable: true,
      },
    ];
  });

  protected readonly fetch: NxDataGridFetchFn<BusinessUnitListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort);

  protected onSelectionChanged(rows: BusinessUnitListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate(['..', rows[0].id], { relativeTo: this.route });
    }
  }
}
