import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, tap } from 'rxjs';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { OrgNameCellComponent } from '../shared/org-name-cell.component';
import { BusinessUnitRow, BusinessUnitsFacade } from './business-units.facade';

/** Legal name of the business unit's legal entity; an em dash when it could not be read. */
export function legalEntityName(row: Pick<BusinessUnitRow, 'legalEntity'> | undefined): string {
  return row?.legalEntity?.legalName || '—';
}

/** Code of the business unit's legal entity (second line of the cell), if it could be read. */
export function legalEntityCode(row: Pick<BusinessUnitRow, 'legalEntity'> | undefined): string | null {
  return row?.legalEntity?.code || null;
}

/**
 * Business-unit list (`conventions-frontend.md §4.4`, list template): header with the record
 * count and «New business unit», and the server-paginated `nx-data-grid` in a flush card.
 *
 * No filter bar: `GET /v1/business-units` only takes `page`/`pageSize`/`sort` (no search, no
 * status filter). The API sorts by `code` and `isActive`, so only those two columns are
 * sortable. The name is the `captionI18n` in the active language (columns are a `computed` of
 * the language, so they follow a switch), and the legal entity is shown by code and legal name.
 * A row (click, Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-business-unit-list-page',
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
      <nx-page-header [breadcrumb]="true" [title]="'org.businessUnits.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="new"
          data-testid="bu-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.businessUnits.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.businessUnits.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [cardFields]="cardFields"
          emptyIcon="storefront"
          [emptyMessageKey]="'org.businessUnits.empty'"
          [emptyHintKey]="'org.businessUnits.emptyHint'"
          (rowOpen)="open($event)"
        >
          <a nxEmptyAction mat-stroked-button routerLink="new" *nxCan="'ORG_STRUCTURE:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.businessUnits.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BusinessUnitListPage {
  private readonly facade = inject(BusinessUnitsFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly i18n = inject(NxI18nService);

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  /** Computed so the name column follows the active language. */
  protected readonly columns = computed<NxColDef<BusinessUnitRow>[]>(() => {
    const language = this.i18n.activeLang();
    return [
      {
        field: 'code',
        headerKey: 'org.businessUnits.fields.code',
        cellClass: 'nx-mono',
        width: 180,
        sortable: true,
      },
      {
        colId: 'caption',
        headerKey: 'org.businessUnits.fields.caption',
        valueGetter: (params) => resolveCaption(params.data?.captionI18n, language, params.data?.code ?? ''),
        flex: 2,
        sortable: false,
      },
      {
        colId: 'legalEntity',
        headerKey: 'org.businessUnits.fields.legalEntityId',
        cellRenderer: OrgNameCellComponent,
        cellRendererParams: { primary: legalEntityName, secondary: legalEntityCode },
        flex: 2,
        sortable: false,
      },
      {
        field: 'isActive',
        headerKey: 'org.businessUnits.fields.isActive',
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

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Observable<Page<BusinessUnitRow>> =>
    this.facade.list(page, pageSize, sort).pipe(tap((result) => this.total.set(result.total)));

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<BusinessUnitRow, 'id'>): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }
}
