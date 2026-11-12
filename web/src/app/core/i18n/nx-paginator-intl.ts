import { Injectable, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslocoService } from '@jsverse/transloco';

/**
 * `MatPaginatorIntl` backed by Transloco (`shared.paginator.*`). Labels are re-read and
 * `changes` is emitted whenever the active language (or its translation file) changes, so
 * mounted paginators re-render in the new language.
 */
@Injectable()
export class NxPaginatorIntl extends MatPaginatorIntl {
  private readonly transloco = inject(TranslocoService);

  constructor() {
    super();
    // `selectTranslation()` emits once the active language's file is loaded, and again on every switch.
    this.transloco.selectTranslation().subscribe(() => this.refresh());
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return this.transloco.translate('shared.paginator.rangeLabelEmpty');
    }
    const start = page * pageSize + 1;
    const end = Math.min((page + 1) * pageSize, length);
    return this.transloco.translate('shared.paginator.rangeLabel', { start, end, length });
  };

  private refresh(): void {
    this.itemsPerPageLabel = this.transloco.translate('shared.paginator.itemsPerPage');
    this.nextPageLabel = this.transloco.translate('shared.paginator.nextPage');
    this.previousPageLabel = this.transloco.translate('shared.paginator.previousPage');
    this.firstPageLabel = this.transloco.translate('shared.paginator.firstPage');
    this.lastPageLabel = this.transloco.translate('shared.paginator.lastPage');
    this.changes.next();
  }
}
