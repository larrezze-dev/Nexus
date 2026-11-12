import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';

/** Display name worth showing under the legal name: set and different from it. */
export function secondaryName(row: Pick<LegalEntityListItem, 'legalName' | 'displayName'> | undefined): string | null {
  const display = row?.displayName?.trim();
  return display && display !== row?.legalName?.trim() ? display : null;
}

/**
 * «Legal name» cell of the legal-entity list: the legal name, and the short display name as a
 * muted second line when it exists and differs (e.g. «SAPE smart bud AG» / «SSB Schweiz»).
 */
@Component({
  selector: 'nx-legal-entity-name-cell',
  standalone: true,
  template: `
    <span class="nx-le-name">{{ legalName() }}</span>
    @if (displayName(); as display) {
      <span class="nx-le-name__secondary">{{ display }}</span>
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

    .nx-le-name,
    .nx-le-name__secondary {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .nx-le-name__secondary {
      font: var(--nx-text-label);
      color: var(--nx-color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LegalEntityNameCellComponent implements ICellRendererAngularComp {
  protected readonly legalName = signal('');
  protected readonly displayName = signal<string | null>(null);

  agInit(params: ICellRendererParams<LegalEntityListItem>): void {
    this.refresh(params);
  }

  refresh(params: ICellRendererParams<LegalEntityListItem>): boolean {
    this.legalName.set(params.data?.legalName ?? '');
    this.displayName.set(secondaryName(params.data));
    return true;
  }
}
