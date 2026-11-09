import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AuditTrailFacade } from './audit-trail.facade';

/**
 * Audit timeline of one aggregate (ficha 0.8, track F; `conventions-frontend.md §5`).
 * Reads `GET /v1/audit/{aggregateType}/{aggregateId}` through `AuditTrailFacade` (FE-R1).
 *
 * `aggregateType` is the literal `nexus-aggregate-type` header value the producer used
 * (`legal-entity`, `business_unit`, `warehouse`, `user`).
 *
 * SKELETON (checkpoint 0): public API is fixed; implementation lands in subtask F1.
 */
@Component({
  selector: 'nx-audit-trail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AuditTrailFacade],
  template: '',
})
export class NxAuditTrailComponent {
  readonly aggregateType = input.required<string>();
  readonly aggregateId = input.required<string>();
}
