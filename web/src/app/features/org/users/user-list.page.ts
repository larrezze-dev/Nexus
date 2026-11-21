import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, catchError, of, tap } from 'rxjs';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import type { NxDataGridFetchFn, Page } from '../../../shared/ui/page.model';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { UsersFacade } from './users.facade';

/**
 * Users list (`conventions-frontend.md §4.4`, list template): header with the record count and
 * «New user», and the server-paginated `nx-data-grid` in a flush card. `GET /v1/users` only takes
 * `page`/`pageSize`/`sort` (no `q`/`businessUnitId`/`isActive`), so there is no filter bar. Name,
 * email and status are sortable; language and business unit show names, not the sorted codes.
 * A row (click, Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-user-list-page',
  standalone: true,
  imports: [
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    NxCanDirective,
    NxDataGridComponent,
    NxPageComponent,
    NxPageHeaderComponent,
  ],
  template: `
    <nx-page width="full">
      <nx-page-header [breadcrumb]="true" [title]="'org.users.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="user-list-new"
          *nxCan="'USER:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.users.actions.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'USER:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.users.actions.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetchFn"
          [cardFields]="cardFields"
          emptyIcon="people"
          [emptyMessageKey]="'org.users.empty'"
          (rowOpen)="open($event)"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'USER:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.users.actions.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserListPage {
  private readonly facade = inject(UsersFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  private readonly i18n = inject(NxI18nService);

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

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
      {
        field: 'email',
        headerKey: 'org.users.fields.email',
        sortable: true,
        flex: 2,
        valueFormatter: (params) => (params.value as string | null | undefined) || '—',
      },
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
        width: 140,
        cellRenderer: OrgActiveStatusCellComponent,
      },
    ];
  });

  /** Cards below 600 px: name, email underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'fullName',
    subtitle: 'email',
    status: 'isActive',
  };

  protected readonly fetchFn: NxDataGridFetchFn<UserListItem> = (page, pageSize, sort): Observable<Page<UserListItem>> =>
    this.facade.list(page, pageSize, sort).pipe(tap((result) => this.total.set(result.total)));

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<UserListItem, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
