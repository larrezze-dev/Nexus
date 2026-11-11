import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { auditTrailInstances, clickTab, denyAuditRead, findAuditTab, selectAuditTab, stubAuditTrail } from '../audit-tab.testing';
import { WarehouseDetailPage } from './warehouse-detail.page';
import { WarehouseFacade } from './warehouse.facade';

const warehouse = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  locationId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c61',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  businessUnitId: null,
  code: 'WH-01',
  captionI18n: { de: 'Lager' },
  isActive: true,
} as unknown as Warehouse;

function createFacadeStub(selected: Warehouse | null) {
  return {
    selected: signal(selected).asReadonly(),
    loading: signal(false).asReadonly(),
    saving: signal(false).asReadonly(),
    error: signal<string | null>(null).asReadonly(),
    businessUnitError: signal<string | null>(null).asReadonly(),
    resolvedLocationLabel: signal<string | null>(null).asReadonly(),
    resolvedLegalEntityLabel: signal<string | null>(null).asReadonly(),
    resolvedBusinessUnitLabel: signal<string | null>(null).asReadonly(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
    searchLocations: vi.fn().mockReturnValue(of([])),
    searchLegalEntities: vi.fn().mockReturnValue(of([])),
    searchBusinessUnits: vi.fn().mockReturnValue(() => of([])),
  };
}

async function setupPage(paramMap: Record<string, string>, facade: ReturnType<typeof createFacadeStub>, denyAudit = false) {
  await TestBed.configureTestingModule({
    imports: [
      WarehouseDetailPage,
      TranslocoTestingModule.forRoot({
        langs: { en: {} },
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: WarehouseFacade, useValue: facade },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
      { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
    ],
  }).compileComponents();

  stubAuditTrail(WarehouseDetailPage);
  if (denyAudit) {
    denyAuditRead();
  }
  const fixture: ComponentFixture<WarehouseDetailPage> = TestBed.createComponent(WarehouseDetailPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('WarehouseDetailPage audit tab', () => {
  it('shows an "Auditoría" tab for an existing warehouse and renders nx-audit-trail when selected', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));
    const audit = await selectAuditTab(fixture);

    expect(audit).not.toBeNull();
    expect(audit?.aggregateType()).toBe('warehouse');
    expect(audit?.aggregateId()).toBe(warehouse.id);
  });

  it('has no "Auditoría" tab when creating a new warehouse', async () => {
    const fixture = await setupPage({ id: 'new' }, createFacadeStub(null));
    expect(findAuditTab(fixture)).toBeUndefined();
  });

  it('has no "Auditoría" tab without AUDIT:READ', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse), true);
    expect(findAuditTab(fixture)).toBeUndefined();
  });

  it('calls facade.reset() before facade.load() in edit mode', async () => {
    const facade = createFacadeStub(warehouse);
    await setupPage({ id: warehouse.id }, facade);
    expect(facade.reset.mock.invocationCallOrder[0]).toBeLessThan(facade.load.mock.invocationCallOrder[0]);
  });

  it('does not show the audit tab nor a foreign id when selected is another record (failed load)', async () => {
    const other = { ...warehouse, id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c99' } as Warehouse;
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(other));
    expect(findAuditTab(fixture)).toBeUndefined();
    expect(auditTrailInstances(fixture)).toHaveLength(0);
  });

  it('re-creates nx-audit-trail every time the audit tab is re-selected', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));
    const first = await selectAuditTab(fixture);
    await clickTab(fixture, 'common.tabs.general');
    await clickTab(fixture, 'common.tabs.audit');
    const second = await selectAuditTab(fixture);

    expect(first).not.toBeNull();
    expect(second).not.toBeNull();
    expect(second).not.toBe(first);
  });

  it('keeps the General form values across General -> Auditoría -> General', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));
    const input = () =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-testid="warehouse-code-input"]');
    input()!.value = 'WH-EDITED';
    input()!.dispatchEvent(new Event('input'));
    await selectAuditTab(fixture);
    await clickTab(fixture, 'common.tabs.general');

    expect(input()?.value).toBe('WH-EDITED');
    expect(fixture.componentInstance['form'].controls.code.value).toBe('WH-EDITED');
  });
});
