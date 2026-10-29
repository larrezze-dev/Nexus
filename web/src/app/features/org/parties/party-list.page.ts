import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoModule } from '@jsverse/transloco';
import type { ColDef } from 'ag-grid-community';
import { NxCanDirective } from '../../../shared/auth/can.directive';
import { NxDataGridComponent } from '../../../shared/ui/nx-data-grid/nx-data-grid.component';
import type { NxDataGridFetchFn } from '../../../shared/ui/page.model';
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import { PartyStatusCellRendererComponent } from './party-status-cell-renderer.component';
import { PartiesFacade } from './parties.facade';

/**
 * Parties directory list (`*-list.page`, `conventions-frontend.md §4`). `GET /v1/parties`
 * supports three filters (`q`, `partyType`, `isInternal`); all three feed `nx-data-grid`'s
 * `[filters]` input, which resets to page 0 on every change.
 */
@Component({
  selector: 'nx-party-list-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    NxCanDirective,
    NxDataGridComponent,
  ],
  templateUrl: './party-list.page.html',
  styles: [
    `
      .nx-party-list-page__header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 1rem;
      }

      .nx-party-list-page__filters {
        display: flex;
        gap: 1rem;
        flex-wrap: wrap;
        margin-bottom: 1rem;
      }
    `,
  ],
})
export class PartyListPage {
  private readonly facade = inject(PartiesFacade);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly filterForm = new FormGroup({
    q: new FormControl('', { nonNullable: true }),
    partyType: new FormControl<'' | 'ORGANIZATION' | 'PERSON'>('', { nonNullable: true }),
    isInternal: new FormControl<'' | 'true' | 'false'>('', { nonNullable: true }),
  });

  private readonly filtersSignal = signal<Record<string, unknown>>({});

  protected readonly filters = computed(() => this.filtersSignal());

  protected readonly columns: ColDef<PartyListItem>[] = [
    { field: 'legalName', headerName: 'Legal name', sortable: true, flex: 2 },
    { field: 'displayName', headerName: 'Display name', sortable: true, flex: 2 },
    { field: 'partyType', headerName: 'Type', sortable: true, flex: 1 },
    { field: 'isInternal', headerName: 'Internal', sortable: false, flex: 1 },
    {
      field: 'isActive',
      headerName: 'Status',
      sortable: false,
      flex: 1,
      cellRenderer: PartyStatusCellRendererComponent,
    },
  ];

  protected readonly fetchFn: NxDataGridFetchFn<PartyListItem> = (page, pageSize, sort, filters) =>
    this.facade.list(page, pageSize, sort, filters);

  constructor() {
    this.filterForm.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      this.filtersSignal.set({
        q: value.q || undefined,
        partyType: value.partyType || undefined,
        isInternal: value.isInternal === '' ? undefined : value.isInternal === 'true',
      });
    });
  }

  protected onSelectionChanged(rows: PartyListItem[]): void {
    if (rows.length === 1) {
      void this.router.navigate([rows[0].id], { relativeTo: this.route });
    }
  }
}
