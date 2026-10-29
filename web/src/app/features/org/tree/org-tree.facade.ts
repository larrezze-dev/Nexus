import { Injectable, inject, signal } from '@angular/core';
import { Api } from '../../../api/platform/api';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { listGroups } from '../../../api/platform/fn/groups/list-groups';
import { listLegalEntities } from '../../../api/platform/fn/legal-entities/list-legal-entities';
import { listWarehouses } from '../../../api/platform/fn/warehouses/list-warehouses';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { GroupListItem } from '../../../api/platform/models/group-list-item';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { WarehouseListItem } from '../../../api/platform/models/warehouse-list-item';
import { isProblem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../shared/caption.util';
import { OrgTreeNode } from './org-tree.model';

/** Max `pageSize` the contract allows (`_shared/components.yaml`); minimizes the number of
 * pages fetched per level. */
const MAX_PAGE_SIZE = 200;

/**
 * Builds the read-only org structure tree (Group -> LegalEntity -> BusinessUnit -> Warehouse).
 *
 * There is no single "tree" endpoint (ficha 0.4): `groups`, `legal-entities` and
 * `business-units` don't even support filtering by parent id in this contract version (only
 * `warehouses` does, via `legalEntityId`/`businessUnitId`). So instead of one HTTP call per
 * tree node (which would be N+1 and get worse as the org grows), this facade fetches each of
 * the four levels **once** in full (paginating only if a level has more than `MAX_PAGE_SIZE`
 * rows, which master data of this kind is not expected to) and assembles the tree in memory
 * from the parent-id fields every list item already carries (`groupId`, `legalEntityId`,
 * `businessUnitId`). Total HTTP calls = 4 (one per level) + 1 extra per level only once that
 * level's row count exceeds 200.
 */
@Injectable({ providedIn: 'root' })
export class OrgTreeFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly nodesSignal = signal<OrgTreeNode[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly nodes = this.nodesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async load(): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    try {
      const [groups, legalEntities, businessUnits, warehouses] = await Promise.all([
        this.fetchAllPages<GroupListItem>((page) =>
          this.api.invoke(listGroups, { page, pageSize: MAX_PAGE_SIZE }),
        ),
        this.fetchAllPages<LegalEntityListItem>((page) =>
          this.api.invoke(listLegalEntities, { page, pageSize: MAX_PAGE_SIZE }),
        ),
        this.fetchAllPages<BusinessUnitListItem>((page) =>
          this.api.invoke(listBusinessUnits, { page, pageSize: MAX_PAGE_SIZE }),
        ),
        this.fetchAllPages<WarehouseListItem>((page) =>
          this.api.invoke(listWarehouses, { page, pageSize: MAX_PAGE_SIZE }),
        ),
      ]);

      this.nodesSignal.set(this.assemble(groups, legalEntities, businessUnits, warehouses));
    } catch (error: unknown) {
      this.nodesSignal.set([]);
      this.errorSignal.set(this.describe(error));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** Fetches every page of a listing (1-based `page`, per the contract) until it has all
   * `total` rows, or the server returns an empty page (defensive, avoids an infinite loop). */
  private async fetchAllPages<T>(
    loadPage: (page: number) => Promise<{ items?: T[]; total: number }>,
  ): Promise<T[]> {
    const collected: T[] = [];
    let page = 1;

    for (;;) {
      const response = await loadPage(page);
      const items = response.items ?? [];
      collected.push(...items);
      if (items.length === 0 || collected.length >= response.total) {
        return collected;
      }
      page += 1;
    }
  }

  private assemble(
    groups: readonly GroupListItem[],
    legalEntities: readonly LegalEntityListItem[],
    businessUnits: readonly BusinessUnitListItem[],
    warehouses: readonly WarehouseListItem[],
  ): OrgTreeNode[] {
    const language = this.i18n.getActiveLanguage();

    const legalEntitiesByGroup = groupBy(legalEntities, (le) => le.groupId);
    const businessUnitsByLe = groupBy(businessUnits, (bu) => bu.legalEntityId);
    const warehousesByBu = groupBy(
      warehouses.filter((wh) => !!wh.businessUnitId),
      (wh) => wh.businessUnitId as string,
    );
    // A warehouse assigned directly to a LegalEntity (no businessUnitId, ORG §3) is nested
    // under its LegalEntity node instead, alongside that LE's BusinessUnit nodes.
    const warehousesByLeDirect = groupBy(
      warehouses.filter((wh) => !wh.businessUnitId),
      (wh) => wh.legalEntityId,
    );

    const toWarehouseNode = (warehouse: WarehouseListItem): OrgTreeNode => ({
      id: warehouse.id,
      kind: 'warehouse',
      code: warehouse.code,
      label: resolveCaption(warehouse.captionI18n, language, warehouse.code),
      isActive: warehouse.isActive,
      children: [],
    });

    const toBusinessUnitNode = (businessUnit: BusinessUnitListItem): OrgTreeNode => ({
      id: businessUnit.id,
      kind: 'businessUnit',
      code: businessUnit.code,
      label: resolveCaption(businessUnit.captionI18n, language, businessUnit.code),
      isActive: businessUnit.isActive,
      children: (warehousesByBu.get(businessUnit.id) ?? []).map(toWarehouseNode),
    });

    const toLegalEntityNode = (legalEntity: LegalEntityListItem): OrgTreeNode => ({
      id: legalEntity.id,
      kind: 'legalEntity',
      code: legalEntity.code,
      label: legalEntity.code,
      isActive: legalEntity.isActive,
      children: [
        ...(businessUnitsByLe.get(legalEntity.id) ?? []).map(toBusinessUnitNode),
        ...(warehousesByLeDirect.get(legalEntity.id) ?? []).map(toWarehouseNode),
      ],
    });

    return groups.map((group) => ({
      id: group.id,
      kind: 'group',
      code: group.code,
      label: group.code,
      isActive: null,
      children: (legalEntitiesByGroup.get(group.id) ?? []).map(toLegalEntityNode),
    }));
  }

  private describe(error: unknown): string {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body) && body.detail) {
        return body.detail;
      }
    }
    return this.i18n.translateErrorCode(undefined);
  }
}

function groupBy<T>(items: readonly T[], keyFn: (item: T) => string): Map<string, T[]> {
  const map = new Map<string, T[]>();
  for (const item of items) {
    const key = keyFn(item);
    const bucket = map.get(key);
    if (bucket) {
      bucket.push(item);
    } else {
      map.set(key, [item]);
    }
  }
  return map;
}
