import { Observable } from 'rxjs';

/**
 * Generic offset-pagination envelope used by every server-paginated listing.
 * Matches the `{ items, page, pageSize, total }` shape from
 * `conventions-api.md §4` so any generated API client's list response can be
 * mapped 1:1 into this type.
 */
export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly total: number;
}

/** Single-column sort state reported by `nx-data-grid`. */
export interface NxSortModel {
  readonly colId: string;
  readonly direction: 'asc' | 'desc';
}

/**
 * Signature every `nx-data-grid` consumer must implement to fetch a page of
 * rows. `page` is 0-based. `filters` is caller-defined (search `q`, status,
 * …) and is passed through untouched.
 */
export type NxDataGridFetchFn<T> = (
  page: number,
  pageSize: number,
  sort: NxSortModel | null,
  filters: Record<string, unknown>,
) => Observable<Page<T>>;
