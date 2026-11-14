import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { catchError, of } from 'rxjs';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxColDef, NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import type { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { UserStatusCellRendererComponent } from './user-status-cell-renderer.component';
import { UsersFacade } from './users.facade';

/**
 * Users list (`*-list.page`, `conventions-frontend.md §4`). `GET /v1/users` (step 0.4 contract)
 * only supports `page`/`pageSize`/`sort` — no `q`/`businessUnitId`/`isActive` filters — so this
 * page has no search bar, just the grid and the "New" action.
 */
@Component({
  selector: 'nx-user-list-page',
  standalone: true,
  imports: [RouterLink, TranslocoModule, MatButtonModule, MatIconModule, NxCanDirective, NxDataGridComponent],
  template: `
    <div class="nx-user-list-page__header">
      <h1>{{ 'org.users.title' | transloco }}</h1>
      <a mat-flat-button color="primary" routerLink="new" *nxCan="'USER:CREATE'">
        <mat-icon>add</mat-icon>
        {{ 'org.users.actions.new' | transloco }}
      </a>
    </div>

    <nx-data-grid
      [columns]="columns()"
      [fetch]="fetchFn"
      (selectionChanged)="onSelectionChanged($event)"
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [
    `
      .nx-user-list-page__header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 1rem;
      }
    `,
  ],
})
export class UserListPage {
  private readonly facade = inject(UsersFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly i18n = inject(NxI18nService);

  /** Business units for the name column; empty if they cannot be read (the cell then shows «—»). */
  private readonly businessUnits = toSignal(
    this.facade.businessUnits().pipe(catchError(() => of<BusinessUnitListItem[]>([]))),
    { initialValue: [] as BusinessUnitListItem[] },
  );
  private readonly businessUnitById = computed(() => new Map(this.businessUnits().map((bu) => [bu.id, bu])));

  /**
   * Re-built when the language or the business-unit list change: the preferred language reads
   * by its name in the active language («Spanisch»/«Español»), never the code, and the business
   * unit by its caption.
   */
  protected readonly columns = computed<NxColDef<UserListItem>[]>(() => {
    const language = this.i18n.activeLang();
    const units = this.businessUnitById();
    return [
      { field: 'fullName', headerKey: 'org.users.fields.fullName', sortable: true, flex: 2 },
      { field: 'email', headerKey: 'org.users.fields.email', sortable: true, flex: 2 },
      {
        colId: 'preferredLanguage',
        headerKey: 'org.users.fields.preferredLanguage',
        valueGetter: (params) => this.facade.languageName(params.data?.preferredLanguage),
        sortable: false,
        flex: 1,
      },
      {
        colId: 'businessUnit',
        headerKey: 'org.users.fields.businessUnit',
        valueGetter: (params) => {
          const unit = params.data?.businessUnitId ? units.get(params.data.businessUnitId) : undefined;
          return unit ? resolveCaption(unit.captionI18n, language, unit.code) : '—';
        },
        sortable: false,
        flex: 1,
      },
      {
        field: 'isActive',
        headerKey: 'org.users.fields.isActive',
        sortable: true,
        flex: 1,
        cellRenderer: UserStatusCellRendererComponent,
      },
    ];
  });

  protected readonly fetchFn: NxDataGridFetchFn<UserListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort);

  protected onSelectionChanged(rows: UserListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate([rows[0].id], { relativeTo: this.route });
    }
  }
}
