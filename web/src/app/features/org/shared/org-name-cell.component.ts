import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';

/** Display name worth showing under the legal name: set and different from it. */
export function secondaryName(row: { legalName?: string | null; displayName?: string | null } | undefined): string | null {
  const display = row?.displayName?.trim();
  return display && display !== row?.legalName?.trim() ? display : null;
}

/**
 * Params of {@link OrgNameCellComponent}: the main line and the optional muted second line of
 * a row. An empty or null second line is not drawn.
 */
export interface OrgNameCellParams<T = unknown> extends ICellRendererParams<T> {
  primary: (row: T | undefined) => string;
  secondary?: (row: T | undefined) => string | null | undefined;
}

/**
 * Two-line name cell of the org lists: a main line and an optional muted second line, e.g. «SAPE
 * smart bud AG» / «SSB Schweiz» (legal entities, parties) or a business unit's legal entity
 * name / its code.
 */
@Component({
  selector: 'nx-org-name-cell',
  standalone: true,
  template: `
    <span class="nx-org-name">{{ primary() }}</span>
    @if (secondary(); as second) {
      <span class="nx-org-name__secondary">{{ second }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      justify-content: center;
      height: 100%;
      min-width: 0;
      line-height: var(--nx-text-body-line);
    }

    .nx-org-name,
    .nx-org-name__secondary {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nx-org-name__secondary {
      font: var(--nx-text-label);
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrgNameCellComponent implements ICellRendererAngularComp {
  protected readonly primary = signal('');
  protected readonly secondary = signal<string | null>(null);

  agInit(params: OrgNameCellParams): void {
    this.refresh(params);
  }

  refresh(params: OrgNameCellParams): boolean {
    this.primary.set(params.primary(params.data) ?? '');
    this.secondary.set(params.secondary?.(params.data) || null);
    return true;
  }
}
