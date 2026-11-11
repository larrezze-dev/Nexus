import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { denyAuditRead, findAuditTab, selectAuditTab, stubAuditTrail } from '../audit-tab.testing';
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
});
