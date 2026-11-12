import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { RoleListItem } from '../../../api/platform/models/role-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { OrgActiveStatusCellComponent } from '../../org/shared/org-active-status-cell.component';
import { RolesFacade } from './roles.facade';

/**
 * `Role` list (`access.md §4`, `conventions-frontend.md §4`). `GET
 * /v1/roles` has `q`/`isActive` query params in the contract, but this
 * subtask keeps the list minimal (no search box yet), same pattern as
 * `LegalEntityListPage` — only server-side pagination and column sort via
 * `nx-data-grid`.
 *
 * Columns follow the active UI language (`captionI18n` resolved via
 * `resolveCaption`), so they are computed, not a static array, and re-run
 * whenever `NxI18nService.activeLang` changes (same pattern as
 * `CurrencyListPage`).
 *
 * `nx-data-grid` only exposes a `(selectionChanged)` event, not a per-row
 * click/open event, so "open a row" is wired through single-row selection:
 * selecting exactly one row navigates straight to its detail page.
 */
@Component({
  selector: 'nx-role-list-page',
  standalone: true,
  imports: [RouterLink, TranslocoModule, NxCanDirective, NxDataGridComponent, MatButtonModule, MatIconModule],
  template: `
    <div class="nx-role-list-page">
      <header class="nx-role-list-page__toolbar">
        <h1>{{ 'access.roles.title' | transloco }}</h1>
        <a mat-raised-button color="primary" routerLink="new" data-testid="role-list-new" *nxCan="'ROLE:CREATE'">
          <mat-icon>add</mat-icon>
          {{ 'access.roles.new' | transloco }}
        </a>
      </header>

      <nx-data-grid
        [columns]="columns()"
        [fetch]="fetch"
        [emptyMessageKey]="'access.roles.empty'"
        (selectionChanged)="onSelectionChanged($event)"
      />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleListPage {
  protected readonly facade = inject(RolesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  private readonly i18n = inject(NxI18nService);

  protected readonly columns = computed<ColDef<RoleListItem>[]>(() => {
    this.i18n.activeLang();
    const language = this.i18n.getActiveLanguage();

    return [
      { field: 'code', headerName: this.transloco.translate('access.roles.fields.code'), sortable: true },
      {
        headerName: this.transloco.translate('access.roles.fields.captionI18n'),
        valueGetter: (params) => resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
      },
      {
        headerName: this.transloco.translate('access.roles.fields.isSystem'),
        valueGetter: (params) =>
          params.data?.isSystem ? this.transloco.translate('common.yes') : this.transloco.translate('common.no'),
      },
      {
        field: 'isActive',
        headerName: this.transloco.translate('access.roles.fields.isActive'),
        cellRenderer: OrgActiveStatusCellComponent,
        sortable: true,
      },
    ];
  });

  protected readonly fetch: NxDataGridFetchFn<RoleListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort);

  protected onSelectionChanged(rows: RoleListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate(['..', rows[0].id], { relativeTo: this.route });
    }
  }
}
