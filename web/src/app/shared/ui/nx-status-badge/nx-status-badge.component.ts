import { NgClass } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { TranslocoModule } from '@jsverse/transloco';

/** Visual + i18n mapping for one status value. */
export interface NxStatusBadgeConfig {
  readonly colorClass: string;
  readonly i18nKey: string;
}

/**
 * Reasonable generic fallback used when the caller does not pass its own
 * `statusConfig`. Domain features are expected to supply their own map
 * (state machine → color/i18n key) once they have real states to show.
 */
const DEFAULT_STATUS_CONFIG: Readonly<Record<string, NxStatusBadgeConfig>> = {
  draft: { colorClass: 'nx-status-badge--neutral', i18nKey: 'shared.status.draft' },
  active: { colorClass: 'nx-status-badge--success', i18nKey: 'shared.status.active' },
  inactive: { colorClass: 'nx-status-badge--neutral', i18nKey: 'shared.status.inactive' },
  pending: { colorClass: 'nx-status-badge--warning', i18nKey: 'shared.status.pending' },
  approved: { colorClass: 'nx-status-badge--success', i18nKey: 'shared.status.approved' },
  rejected: { colorClass: 'nx-status-badge--danger', i18nKey: 'shared.status.rejected' },
};

/** Generic "state → color + i18n text" badge, reused by any state machine. */
@Component({
  selector: 'nx-status-badge',
  standalone: true,
  imports: [NgClass, MatChipsModule, TranslocoModule],
  templateUrl: './nx-status-badge.component.html',
  styleUrl: './nx-status-badge.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxStatusBadgeComponent {
  readonly status = input.required<string>();

  /** Override the default status → {colorClass, i18nKey} map. */
  readonly statusConfig = input<Record<string, NxStatusBadgeConfig>>();

  protected readonly resolved = computed<NxStatusBadgeConfig>(() => {
    const config = this.statusConfig() ?? DEFAULT_STATUS_CONFIG;
    return (
      config[this.status()] ?? {
        colorClass: 'nx-status-badge--neutral',
        i18nKey: `shared.status.${this.status()}`,
      }
    );
  });
}
