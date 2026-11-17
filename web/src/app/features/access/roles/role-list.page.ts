import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { tap } from 'rxjs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { RoleListItem } from '../../../api/platform/models/role-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { OrgActiveStatusCellComponent } from '../../org/shared/org-active-status-cell.component';
import { RolesFacade } from './roles.facade';

/**
 * `Role` list (`access.md §4`, `conventions-frontend.md §4.4`, list template): header with the
 * record count and «New role» (an extended FAB below 600 px), and the server-paginated
 * `nx-data-grid` in a flush card. A row (click, Enter, card tap) opens the detail page.
 *
 * No filter bar yet: this step keeps the list minimal (pagination and sort only), like the
 * business-unit list. The name is the `captionI18n` in the active language (columns are a
 * `computed` of the language, so they follow a switch).
 */
@Component({
  selector: 'nx-role-list-page',
  standalone: true,
  imports: [
    RouterLink,
    TranslocoModule,
    NxCanDirective,
    NxDataGridComponent,
    NxPageComponent,
    NxPageHeaderComponent,
    MatButtonModule,
    MatIconModule,
  ],
  template: `
    <nx-page width="full">
      <nx-page-header [breadcrumb]="true" [title]="'access.roles.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="role-list-new"
          *nxCan="'ROLE:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'access.roles.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'ROLE:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'access.roles.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [cardFields]="cardFields"
          emptyIcon="admin_panel_settings"
          [emptyMessageKey]="'access.roles.empty'"
          (rowOpen)="open($event)"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'ROLE:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'access.roles.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleListPage {
  protected readonly facade = inject(RolesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly transloco = inject(TranslocoService);
  private readonly i18n = inject(NxI18nService);
  /** Emits once the active language's file is loaded (and on every switch): the yes/no texts follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  protected readonly columns = computed<NxColDef<RoleListItem>[]>(() => {
    const language = this.i18n.activeLang();
    this.translation();
    const yes = this.transloco.translate('common.yes');
    const no = this.transloco.translate('common.no');

    return [
      {
        field: 'code',
        headerKey: 'access.roles.fields.code',
        cellClass: 'nx-mono',
        width: 220,
        sortable: true,
      },
      {
        colId: 'caption',
        headerKey: 'access.roles.fields.captionI18n',
        valueGetter: (params) => resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
        flex: 2,
        sortable: false,
      },
      {
        colId: 'isSystem',
        headerKey: 'access.roles.fields.isSystem',
        valueGetter: (params) => (params.data?.isSystem ? yes : no),
        width: 160,
        sortable: false,
      },
      {
        field: 'isActive',
        headerKey: 'access.roles.fields.isActive',
        cellRenderer: OrgActiveStatusCellComponent,
        width: 140,
        sortable: true,
      },
    ];
  });

  /** Cards below 600 px: name, code underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'caption',
    subtitle: 'code',
    status: 'isActive',
  };

  protected readonly fetch: NxDataGridFetchFn<RoleListItem> = (page, pageSize, sort) =>
    this.facade.list(page, pageSize, sort).pipe(tap((result) => this.total.set(result.total)));

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<RoleListItem, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
