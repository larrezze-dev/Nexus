import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AuditEvent } from '../../../api/platform/models/audit-event';
import { NxFormatService } from '../../../core/i18n/nx-format.service';
import { AuditDiff, AuditDiffRow, computeAuditDiff, isUuid, shortUuid } from './audit-diff';
import { AuditTrailFacade } from './audit-trail.facade';

/** A rendered table cell. */
export interface AuditCell {
  readonly text: string;
  readonly empty: boolean;
  readonly mono: boolean;
  readonly title: string | null;
}

export interface AuditRowView {
  readonly key: string;
  readonly label: string;
  readonly before: AuditCell;
  readonly after: AuditCell;
}

export type AuditDiffView =
  | { readonly kind: 'pii' }
  | { readonly kind: 'initial'; readonly rows: readonly AuditRowView[] }
  | { readonly kind: 'current'; readonly rows: readonly AuditRowView[] }
  | { readonly kind: 'changes'; readonly rows: readonly AuditRowView[] };

export interface AuditEventView {
  readonly event: AuditEvent;
  readonly kind: 'created' | 'updated' | 'deactivated' | 'other';
  readonly icon: string;
  readonly title: string;
  readonly actor: string | null;
  readonly date: string;
  readonly diff: AuditDiffView;
}

const KIND_ICONS = {
  created: 'add',
  updated: 'edit',
  deactivated: 'block',
  other: 'fiber_manual_record',
} as const;

/**
 * Audit timeline of one aggregate (ficha 0.8, track F; `conventions-frontend.md §5`).
 * Reads `GET /v1/audit/{aggregateType}/{aggregateId}` through `AuditTrailFacade` (FE-R1).
 *
 * `aggregateType` is the literal `nexus-aggregate-type` header value the producer used
 * (`legal-entity`, `business_unit`, `warehouse`, `user`). Loads on init (the parent creates the
 * component each time its tab is selected) and on the refresh button; no polling.
 */
@Component({
  selector: 'nx-audit-trail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AuditTrailFacade],
  imports: [NgTemplateOutlet, TranslocoModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './nx-audit-trail.component.html',
  styleUrl: './nx-audit-trail.component.scss',
})
export class NxAuditTrailComponent {
  readonly aggregateType = input.required<string>();
  readonly aggregateId = input.required<string>();

  protected readonly facade = inject(AuditTrailFacade);
  private readonly transloco = inject(TranslocoService);
  private readonly format = inject(NxFormatService);
  /** Emits once the translations of the active language are loaded (not merely when it is switched). */
  private readonly translations = toSignal(this.transloco.selectTranslation(), { initialValue: undefined });

  protected readonly views = computed<readonly AuditEventView[]>(() => {
    this.translations();
    const labels = this.facade.resolvedLabels();
    return this.facade.items().map((event) => this.toView(event, labels));
  });

  constructor() {
    effect(() => {
      const type = this.aggregateType();
      const id = this.aggregateId();
      untracked(() => void this.facade.load(type, id));
    });
  }

  protected reload(): void {
    void this.facade.reload();
  }

  protected retry(): void {
    void this.facade.retry();
  }

  protected loadMore(): void {
    void this.facade.loadMore();
  }

  private toView(event: AuditEvent, labels: Readonly<Record<string, string>>): AuditEventView {
    const last = event.eventType.split('.').pop();
    const kind = last === 'created' || last === 'updated' || last === 'deactivated' ? last : 'other';
    return {
      event,
      kind,
      icon: KIND_ICONS[kind],
      title: this.translateOr(`events.${event.eventType}`, event.eventType),
      actor: event.actorDisplayName,
      date: this.format.formatDate(event.occurredAt, { second: '2-digit', hourCycle: 'h23' }),
      diff: this.toDiffView(computeAuditDiff(event.payload, event.previousPayload), kind, labels),
    };
  }

  private toDiffView(
    diff: AuditDiff,
    eventKind: AuditEventView['kind'],
    labels: Readonly<Record<string, string>>,
  ): AuditDiffView {
    if (diff.kind === 'pii') {
      return diff;
    }
    const rows = diff.rows.map((row) => this.toRowView(row, labels));
    // "Initial values" only makes sense for a creation; any other event without a previous
    // payload (e.g. its predecessor was never audited) shows the current values with a notice.
    const kind = diff.kind === 'initial' && eventKind !== 'created' ? 'current' : diff.kind;
    return { kind, rows };
  }

  private toRowView(row: AuditDiffRow, labels: Readonly<Record<string, string>>): AuditRowView {
    const base = this.translateOr(`audit.fields.${row.field}`, row.field);
    return {
      key: row.path,
      label: row.subKey ? `${base} (${row.subKey})` : base,
      before: this.toCell(row.field, row.before, labels),
      after: this.toCell(row.field, row.after, labels),
    };
  }

  private toCell(field: string, value: unknown, labels: Readonly<Record<string, string>>): AuditCell {
    if (value === null || value === undefined || value === '') {
      return { text: this.transloco.translate('audit.emptyValue'), empty: true, mono: false, title: null };
    }
    if (typeof value === 'boolean') {
      return { text: this.transloco.translate(value ? 'common.yes' : 'common.no'), empty: false, mono: false, title: null };
    }
    if (field.endsWith('Id') && isUuid(value)) {
      const label = labels[value];
      return label
        ? { text: label, empty: false, mono: false, title: value }
        : { text: shortUuid(value), empty: false, mono: true, title: value };
    }
    const text = typeof value === 'object' ? JSON.stringify(value) : String(value);
    return { text, empty: false, mono: false, title: null };
  }

  private translateOr(key: string, fallback: string): string {
    const translated = this.transloco.translate(key);
    return translated === key || typeof translated !== 'string' ? fallback : translated;
  }
}
