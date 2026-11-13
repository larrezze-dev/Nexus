import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, tap } from 'rxjs';
import { LocationListItem } from '../../../api/platform/models/location-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { countryName } from '../../../shared/ui/country/country-names';
import { NxPageComponent, NxPageHeaderComponent } from '../../../shared/ui/layout';
import {
  NxColDef,
  NxDataGridCardFields,
  NxDataGridComponent,
} from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import { NxSortModel, Page } from '../../../shared/ui/page.model';
import { OrgActiveStatusCellComponent } from '../shared/org-active-status-cell.component';
import { LocationFacade } from './location.facade';

/**
 * Location list (`conventions-frontend.md §4.4`, list template): header with the record count
 * and «New location», and the server-paginated `nx-data-grid` in a flush card.
 *
 * No filter bar: `GET /v1/locations` only takes `page`/`pageSize`/`sort` (no search, no status
 * filter). The API sorts by `name`, `city` and `isActive` (default: name), so only those
 * columns are sortable. The country is shown by its name in the active language (columns are a
 * `computed` of the language, so they follow a switch), never as the bare ISO code. A row
 * (click, Enter, card tap) opens the detail page.
 */
@Component({
  selector: 'nx-location-list-page',
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
      <nx-page-header [breadcrumb]="true" [title]="'org.locations.title' | transloco" [count]="total()">
        <a
          nxHeaderActions
          mat-flat-button
          class="nx-create--header"
          routerLink="/locations/new"
          data-testid="location-list-new"
          *nxCan="'ORG_STRUCTURE:CREATE'"
        >
          <mat-icon aria-hidden="true">add</mat-icon>
          {{ 'org.locations.new' | transloco }}
        </a>
      </nx-page-header>
      <!-- Below 600 px «New» is an extended FAB (components.css .nx-create--*). -->
      <a mat-flat-button class="nx-create--fab" routerLink="/locations/new" *nxCan="'ORG_STRUCTURE:CREATE'">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'org.locations.new' | transloco }}
      </a>

      <section class="nx-card nx-card--flush">
        <nx-data-grid
          [columns]="columns()"
          [fetch]="fetch"
          [cardFields]="cardFields"
          emptyIcon="place"
          [emptyMessageKey]="'org.locations.empty'"
          [emptyHintKey]="'org.locations.emptyHint'"
          (rowOpen)="open($event)"
        >
          <a nxEmptyAction mat-stroked-button routerLink="/locations/new" *nxCan="'ORG_STRUCTURE:CREATE'">
            <mat-icon aria-hidden="true">add</mat-icon>
            {{ 'org.locations.new' | transloco }}
          </a>
        </nx-data-grid>
      </section>
    </nx-page>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationListPage {
  private readonly facade = inject(LocationFacade);
  private readonly router = inject(Router);
  private readonly i18n = inject(NxI18nService);

  /** Total of the last loaded page («N records» in the header); `null` until the first load. */
  protected readonly total = signal<number | null>(null);

  /** Computed so the country column follows the active language. */
  protected readonly columns = computed<NxColDef<LocationListItem>[]>(() => {
    const language = this.i18n.activeLang();
    return [
      {
        field: 'code',
        headerKey: 'org.locations.fields.code',
        cellClass: 'nx-mono',
        valueGetter: (params) => params.data?.code || '—',
        width: 160,
        sortable: false,
      },
      { field: 'name', headerKey: 'org.locations.fields.name', flex: 2, sortable: true },
      {
        field: 'city',
        headerKey: 'org.locations.fields.city',
        valueGetter: (params) => params.data?.city || '—',
        flex: 1,
        sortable: true,
      },
      {
        field: 'countryCode',
        headerKey: 'org.locations.fields.countryCode',
        valueGetter: (params) => countryName(params.data?.countryCode, language),
        flex: 1,
        sortable: false,
      },
      {
        field: 'isActive',
        headerKey: 'org.locations.fields.isActive',
        cellRenderer: OrgActiveStatusCellComponent,
        width: 140,
        sortable: true,
      },
    ];
  });

  /** Cards below 600 px: name, code underneath, status on the right. */
  protected readonly cardFields: NxDataGridCardFields = {
    title: 'name',
    subtitle: 'code',
    status: 'isActive',
  };

  protected readonly fetch = (
    page: number,
    pageSize: number,
    sort: NxSortModel | null,
  ): Observable<Page<LocationListItem>> =>
    this.facade.list(page, pageSize, sort).pipe(tap((result) => this.total.set(result.total)));

  /** Opens the detail page of a row (click, Enter or card tap). */
  protected open(row: Pick<LocationListItem, 'id'>): void {
    void this.router.navigate(['/locations', row.id]);
  }
}
