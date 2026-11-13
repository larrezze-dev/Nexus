import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { WritableSignal, signal } from '@angular/core';
import { of } from 'rxjs';
import { Warehouse } from '../../../api/platform/models/warehouse';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import {
  auditTrailInstances,
  clickTab,
  denyAuditRead,
  findAuditTab,
  selectAuditTab,
  stubAuditTrail,
} from '../audit-tab.testing';
import { WarehouseDetailPage } from './warehouse-detail.page';
import { WarehouseFacade } from './warehouse.facade';
import { WarehouseLocationRef, WarehouseSaveError } from './warehouse.models';

const LE_ID = '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40';

const warehouse = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c60',
  locationId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c61',
  legalEntityId: LE_ID,
  businessUnitId: null,
  code: 'WH-01',
  captionI18n: { de: 'Lager' },
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
} as unknown as Warehouse;

const wh = {
  title: 'Warehouses',
  newTitle: 'New warehouse',
  loading: 'Loading…',
  deactivate: 'Deactivate',
  deactivateDialog: { title: 'Deactivate?', message: '{{name}} ({{code}}) will be deactivated.' },
  detail: { modified: 'modified on {{date}}' },
  progress: { creatingLocation: 'Creating location…', savingWarehouse: 'Saving warehouse…' },
  address: { locationCreated: 'Location {{value}} was already created.' },
};
const translations = { en: { org: { warehouses: wh } } };

function createFacadeStub(selected: Warehouse | null) {
  return {
    selected: signal(selected) as WritableSignal<Warehouse | null>,
    loading: signal(false),
    saving: signal(false),
    error: signal<string | null>(null),
    title: signal<string | null>(selected ? 'Lager' : null),
    pendingLocation: signal<WarehouseLocationRef | null>(null),
    saveStep: signal<'idle' | 'creatingLocation' | 'savingWarehouse'>('idle'),
    locationRef: signal<WarehouseLocationRef | null>(
      selected ? { id: warehouse.locationId, code: 'LOC-01', name: 'Lager Zug' } : null,
    ),
    legalEntityRef: signal(selected ? { id: LE_ID, code: 'LE-CH', legalName: 'Nüssli AG' } : null),
    businessUnitRef: signal(null),
    describeError: vi.fn().mockReturnValue({ message: 'err' }),
    displayLocation: (l: { code?: string | null; name: string }) =>
      l.code ? `${l.code} — ${l.name}` : l.name,
    save: vi.fn(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    deactivate: vi.fn(),
    searchLocations: vi.fn().mockReturnValue(of([])),
    searchLegalEntities: vi
      .fn()
      .mockReturnValue(of([{ id: LE_ID, code: 'LE-CH', legalName: 'Nüssli AG', isActive: true }])),
    searchBusinessUnits: vi.fn().mockReturnValue(() => of([])),
  };
}

const confirmStub = { confirm: vi.fn() };

async function setupPage(
  paramMap: Record<string, string>,
  facade: ReturnType<typeof createFacadeStub>,
  denyAudit = false,
) {
  confirmStub.confirm.mockReset();
  await TestBed.configureTestingModule({
    imports: [
      WarehouseDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: WarehouseFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
      { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
      {
        provide: NxBreadcrumbService,
        useValue: {
          setCurrentLabel: vi.fn(),
          claimForPage: () => () => undefined,
          trail: signal([]),
        },
      },
      { provide: NxConfirm, useValue: confirmStub },
    ],
  }).compileComponents();

  stubAuditTrail(WarehouseDetailPage);
  if (denyAudit) {
    denyAuditRead();
  }
  const fixture: ComponentFixture<WarehouseDetailPage> =
    TestBed.createComponent(WarehouseDetailPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

function root(fixture: ComponentFixture<unknown>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function byTestId(fixture: ComponentFixture<unknown>, testId: string): HTMLElement | null {
  return root(fixture).querySelector(`[data-testid="${testId}"]`);
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  fixture.detectChanges();
}

describe('WarehouseDetailPage header and states', () => {
  it('shows the name as title, code · last change as subtitle, the status and Deactivate', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));

    expect(root(fixture).querySelectorAll('h1').length).toBe(1);
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('Lager');
    expect(root(fixture).querySelector('.nx-page-header__subtitle')?.textContent).toContain(
      'WH-01 · modified on',
    );
    expect(root(fixture).querySelector('nx-page-header nx-status-badge')).toBeTruthy();
    expect(
      byTestId(fixture, 'warehouse-deactivate-button')?.closest('nx-page-header'),
    ).toBeTruthy();
  });

  it('shows the current location by name inside the location field and locks the legal entity', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));
    await fixture.componentInstance['load']();
    await settle(fixture);

    expect((byTestId(fixture, 'warehouse-location-input') as HTMLInputElement).value).toBe(
      'LOC-01 — Lager Zug',
    );
    expect(fixture.componentInstance['form'].controls.legalEntityId.disabled).toBe(true);
    expect(root(fixture).querySelector('.nx-field--locked')).toBeTruthy();
  });

  it('deactivates only after the destructive confirmation dialog, naming the record', async () => {
    const facade = createFacadeStub(warehouse);
    facade.deactivate.mockResolvedValue({ ...warehouse, isActive: false });
    const fixture = await setupPage({ id: warehouse.id }, facade);

    confirmStub.confirm.mockResolvedValue(false);
    await fixture.componentInstance['deactivate']();
    expect(confirmStub.confirm).toHaveBeenCalledWith(
      expect.objectContaining({ destructive: true, message: 'Lager (WH-01) will be deactivated.' }),
    );
    expect(facade.deactivate).not.toHaveBeenCalled();

    confirmStub.confirm.mockResolvedValue(true);
    await fixture.componentInstance['deactivate']();
    expect(facade.deactivate).toHaveBeenCalledWith(warehouse.id);
  });

  it('offers no Deactivate on an inactive warehouse (there is no reactivate in the API)', async () => {
    const fixture = await setupPage(
      { id: warehouse.id },
      createFacadeStub({ ...warehouse, isActive: false }),
    );
    expect(byTestId(fixture, 'warehouse-deactivate-button')).toBeNull();
  });

  it('is read-only with a notice and no Deactivate without ORG_STRUCTURE:UPDATE', async () => {
    const fixture = await setupPage({ id: warehouse.id }, createFacadeStub(warehouse));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:READ', scopes: [] }]);
    await settle(fixture);

    expect(byTestId(fixture, 'warehouse-read-only')).toBeTruthy();
    expect(byTestId(fixture, 'warehouse-deactivate-button')).toBeNull();
    expect((byTestId(fixture, 'warehouse-save-button') as HTMLButtonElement).disabled).toBe(true);
    expect(fixture.componentInstance['form'].disabled).toBe(true);
  });

  it('shows the error state with «Retry» when the warehouse cannot be read', async () => {
    const facade = createFacadeStub(null);
    facade.error.set('Something went wrong');
    const fixture = await setupPage({ id: warehouse.id }, facade);
    facade.load.mockClear();

    expect(byTestId(fixture, 'warehouse-load-error')).toBeTruthy();
    byTestId(fixture, 'warehouse-retry-button')?.click();
    expect(facade.load).toHaveBeenCalledWith(warehouse.id);
  });

  it('uses the create template: «New warehouse» header, no tabs', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('New warehouse');
    expect(root(fixture).querySelector('[role="tab"]')).toBeNull();
  });
});

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
    expect(facade.reset.mock.invocationCallOrder[0]).toBeLessThan(
      facade.load.mock.invocationCallOrder[0],
    );
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
    const input = () => byTestId(fixture, 'warehouse-code-input') as HTMLInputElement | null;
    input()!.value = 'WH-EDITED';
    input()!.dispatchEvent(new Event('input'));
    await selectAuditTab(fixture);
    await clickTab(fixture, 'common.tabs.general');

    expect(input()?.value).toBe('WH-EDITED');
    expect(fixture.componentInstance['form'].controls.code.value).toBe('WH-EDITED');
  });
});

describe('WarehouseDetailPage address options and saving', () => {
  it('shows «same as legal entity» disabled with its note and swaps the picker for the new-address fields', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    expect(
      byTestId(fixture, 'warehouse-address-same')?.querySelector('input')?.hasAttribute('disabled'),
    ).toBe(true);
    expect(byTestId(fixture, 'warehouse-address-same')?.textContent).toContain(
      'org.warehouses.address.sameAsLegalEntityUnavailable',
    );
    expect(byTestId(fixture, 'warehouse-location-input')).not.toBeNull();
    expect(byTestId(fixture, 'warehouse-new-location')).toBeNull();

    fixture.componentInstance['form'].controls.addressMode.setValue('newLocation');
    await settle(fixture);

    expect(byTestId(fixture, 'warehouse-location-input')).toBeNull();
    expect(byTestId(fixture, 'warehouse-new-location-name')).not.toBeNull();
    expect(byTestId(fixture, 'warehouse-new-location-countryCode')?.tagName.toLowerCase()).toBe(
      'mat-select',
    );
  });

  it('on an invalid submit shows the number of fields to check and focuses the first one', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    await fixture.componentInstance['save']();
    await settle(fixture);

    // code, legal entity, German name, location
    expect(byTestId(fixture, 'warehouse-form-invalid')?.getAttribute('role')).toBe('alert');
    expect(fixture.componentInstance['invalidCount']()).toBe(4);
    expect(document.activeElement).toBe(byTestId(fixture, 'warehouse-code-input'));
  });

  it('shows the step of a running save', async () => {
    const facade = createFacadeStub(null);
    const fixture = await setupPage({}, facade);
    facade.saveStep.set('creatingLocation');
    fixture.detectChanges();
    expect(byTestId(fixture, 'warehouse-save-progress')?.textContent).toContain(
      'Creating location…',
    );
    facade.saveStep.set('savingWarehouse');
    fixture.detectChanges();
    expect(byTestId(fixture, 'warehouse-save-progress')?.textContent).toContain(
      'Saving warehouse…',
    );
  });

  it('says the new location already exists and offers «Retry» when the warehouse step fails', async () => {
    const facade = createFacadeStub(warehouse);
    const fixture = await setupPage({ id: warehouse.id }, facade);
    await fixture.componentInstance['load']();
    const form = fixture.componentInstance['form'];
    form.controls.code.setValue('WH-02');
    form.markAsDirty();
    const cause = new HttpErrorResponse({ status: 500, headers: new HttpHeaders() });
    facade.save.mockImplementation(async () => {
      facade.pendingLocation.set({ id: 'loc-new', code: 'LOC-NEW', name: 'Nave Getafe' });
      throw new WarehouseSaveError('warehouse', cause, 'loc-new');
    });

    await fixture.componentInstance['save']();
    await settle(fixture);

    const alert = byTestId(fixture, 'warehouse-pending-location');
    expect(alert?.textContent).toContain('Location LOC-NEW — Nave Getafe was already created.');
    facade.save.mockClear();
    (byTestId(fixture, 'warehouse-retry-save') as HTMLButtonElement).click();
    expect(facade.save).toHaveBeenCalled();
  });

  it('shows an ORG-R3 rejection under the business-unit field', async () => {
    const facade = createFacadeStub(warehouse);
    facade.describeError.mockReturnValue({
      message: 'Unit of another legal entity',
      code: 'ORG-R3',
    });
    facade.save.mockRejectedValue(new WarehouseSaveError('warehouse', {}, null));
    const fixture = await setupPage({ id: warehouse.id }, facade);
    await fixture.componentInstance['load']();
    fixture.componentInstance['form'].markAsDirty();

    await fixture.componentInstance['save']();
    await settle(fixture);

    expect(byTestId(fixture, 'warehouse-business-unit-error')?.textContent).toContain(
      'Unit of another legal entity',
    );
    expect(byTestId(fixture, 'warehouse-form-error')).toBeNull();
  });
});
