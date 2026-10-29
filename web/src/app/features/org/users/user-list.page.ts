import { Component, inject } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
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
      [columns]="columns"
      [fetch]="fetchFn"
      (selectionChanged)="onSelectionChanged($event)"
    />
  `,
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

  protected readonly columns: ColDef<UserListItem>[] = [
    { field: 'fullName', headerName: 'Full name', sortable: true, flex: 2 },
    { field: 'email', headerName: 'Email', sortable: true, flex: 2 },
    { field: 'preferredLanguage', headerName: 'Language', sortable: false, flex: 1 },
    {
      field: 'isActive',
      headerName: 'Status',
      sortable: false,
      flex: 1,
      cellRenderer: UserStatusCellRendererComponent,
    },
  ];

  protected readonly fetchFn: NxDataGridFetchFn<UserListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort);

  protected onSelectionChanged(rows: UserListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate([rows[0].id], { relativeTo: this.route });
    }
  }
}
