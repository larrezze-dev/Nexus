import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoService, TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { Party } from '../../../api/platform/models/party';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { denyAuditRead, findAuditTab, selectAuditTab, stubAuditTrail } from '../audit-tab.testing';
import { BusinessUnitDetailPage } from './business-unit-detail.page';
import { BusinessUnitFormValue } from './business-unit-form.component';
import { BusinessUnitsFacade } from './business-units.facade';

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-BCN-RD',
  captionI18n: { de: 'F+E Barcelona', es: 'I+D Barcelona' },
  costCenterCode: null,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const party: Party = {
  id: businessUnit.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud I+D Barcelona',
  displayName: 'I+D Barcelona',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: businessUnit.createdAt,
  updatedAt: businessUnit.updatedAt,
};

const formValue: BusinessUnitFormValue = {
  legalName: 'SAPE smart bud Rental',
  displayName: '',
  legalEntityId: businessUnit.legalEntityId,
  code: 'BU-NEW',
  captionI18n: { de: 'Vermietung' },
  costCenterCode: '',
};

const bu = {
  title: 'Business units',
  newTitle: 'New business unit',
  loading: 'Loading…',
  deactivate: 'Deactivate',
  deactivateDialog: { title: 'Deactivate?', message: '{{name}} ({{code}}) will be deactivated.' },
  deactivated: 'Deactivated',
  created: 'Created',
  saved: 'Saved',
  detail: { modified: 'modified on {{date}}' },
  fields: {},
};

const translations = {
  en: { org: { businessUnits: bu }, errors: { validation: 'Fix the fields', generic: 'Something went wrong' } },
  es: { org: { businessUnits: bu }, errors: { validation: 'Fix the fields', generic: 'Something went wrong' } },
};

function createFacadeStub(initialSelected: BusinessUnit | null, initialParty: Party | null = initialSelected ? party : null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    party: signal(initialParty).asReadonly(),
    loading: signal(false).asReadonly(),
    error: signal<string | null>(null).asReadonly(),
    legalEntityOptions: vi
      .fn()
      .mockResolvedValue([{ id: businessUnit.legalEntityId, code: 'LE-SSB', legalName: 'SAPE smart bud S.L.', isActive: true }]),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
  };
}

const confirmStub = { confirm: vi.fn() };

async function setupPage(paramMap: Record<string, string>, facade: ReturnType<typeof createFacadeStub>) {
  confirmStub.confirm.mockReset();
  await TestBed.configureTestingModule({
    imports: [
      BusinessUnitDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en', 'es'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: BusinessUnitsFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
      {
        provide: NxBreadcrumbService,
        useValue: { setCurrentLabel: vi.fn(), claimForPage: () => () => undefined, trail: signal([]) },
      },
      { provide: NxConfirm, useValue: confirmStub },
    ],
  }).compileComponents();

  stubAuditTrail(BusinessUnitDetailPage);
  // Real NxI18nService (active language signal), only the error-code translation stubbed.
  vi.spyOn(TestBed.inject(NxI18nService), 'translateErrorCode').mockImplementation((code) => code ?? 'generic');
  const fixture: ComponentFixture<BusinessUnitDetailPage> = TestBed.createComponent(BusinessUnitDetailPage);
  return fixture;
}

describe('BusinessUnitDetailPage', () => {
  it('loads the unit in edit mode when the route has an id', async () => {
    const facade = createFacadeStub(businessUnit);
    const fixture = await setupPage({ id: businessUnit.id }, facade);
    fixture.detectChanges();

    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(businessUnit.id);
    expect(facade.legalEntityOptions).toHaveBeenCalled();
  });

  it('does not load anything in create mode (no id param)', async () => {
    const facade = createFacadeStub(null);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    expect(facade.load).not.toHaveBeenCalled();
  });

  it('maps a 400 response into per-field errors and a validation message', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockRejectedValue({ status: 400, error: { errors: { code: ['Code already in use'] } } });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['Code already in use'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
  });

  it('translates a 422 business-rule code via NxI18nService', async () => {
    const facade = createFacadeStub(businessUnit);
    facade.update.mockRejectedValue({ status: 422, error: { code: 'ORG-R5' } });
    const fixture = await setupPage({ id: businessUnit.id }, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(fixture.componentInstance['saveError']()).toBe('ORG-R5');
  });

  it('creates with the party names and opens the created unit', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockResolvedValue(businessUnit);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    const router = TestBed.inject(Router);

    await fixture.componentInstance['onSave'](formValue);

    expect(facade.create).toHaveBeenCalledWith({
      legalName: 'SAPE smart bud Rental',
      displayName: null,
      legalEntityId: businessUnit.legalEntityId,
      code: 'BU-NEW',
      captionI18n: { de: 'Vermietung' },
      costCenterCode: null,
    });
    expect(router.navigate).toHaveBeenCalledWith(['..', businessUnit.id], { relativeTo: expect.anything() });
  });

  it('updates only what the API can change (code, names, cost center)', async () => {
    const facade = createFacadeStub(businessUnit);
    facade.update.mockResolvedValue(businessUnit);
    const fixture = await setupPage({ id: businessUnit.id }, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave']({ ...formValue, costCenterCode: 'CC-9' });

    expect(facade.update).toHaveBeenCalledWith(businessUnit.id, {
      code: 'BU-NEW',
      captionI18n: { de: 'Vermietung' },
      costCenterCode: 'CC-9',
    });
  });

  it('shows the name in the active language as title, code · last change as subtitle and the status', async () => {
    const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub(businessUnit));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('h1').length).toBe(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('F+E Barcelona'); // no «en» → «de»
    expect(root.querySelector('.nx-page-header__subtitle')?.textContent).toContain('BU-BCN-RD · modified on');
    expect(root.querySelector('nx-page-header nx-status-badge')).toBeTruthy();
    expect(root.querySelector('nx-page')?.classList).toContain('nx-page--form');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('I+D Barcelona');
  });

  it('names the record in the breadcrumb and renames it on a language switch', async () => {
    const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub(businessUnit));
    fixture.detectChanges();
    await fixture.whenStable();
    const breadcrumb = TestBed.inject(NxBreadcrumbService);

    expect(breadcrumb.setCurrentLabel).toHaveBeenLastCalledWith('F+E Barcelona');

    TestBed.inject(TranslocoService).setActiveLang('es');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(breadcrumb.setCurrentLabel).toHaveBeenLastCalledWith('I+D Barcelona');
  });

  it('deactivates only after the destructive confirmation dialog, naming the record', async () => {
    const facade = createFacadeStub(businessUnit);
    facade.deactivate.mockResolvedValue({ ...businessUnit, isActive: false });
    const fixture = await setupPage({ id: businessUnit.id }, facade);
    fixture.detectChanges();

    confirmStub.confirm.mockResolvedValue(false);
    await fixture.componentInstance['onDeactivate']();
    expect(confirmStub.confirm).toHaveBeenCalledWith(
      expect.objectContaining({ destructive: true, message: 'F+E Barcelona (BU-BCN-RD) will be deactivated.' }),
    );
    expect(facade.deactivate).not.toHaveBeenCalled();

    confirmStub.confirm.mockResolvedValue(true);
    await fixture.componentInstance['onDeactivate']();
    expect(facade.deactivate).toHaveBeenCalledWith(businessUnit.id);
  });

  it('offers no Deactivate on an inactive unit (there is no reactivate in the API)', async () => {
    const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub({ ...businessUnit, isActive: false }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="bu-detail-deactivate"]')).toBeNull();
  });

  it('is read-only with a notice and no Deactivate without ORG_STRUCTURE:UPDATE', async () => {
    const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub(businessUnit));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:READ', scopes: [] }]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="bu-detail-read-only"]')).toBeTruthy();
    expect(root.querySelector('[data-testid="bu-detail-deactivate"]')).toBeNull();
    expect((root.querySelector('[data-testid="bu-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('uses the create template: «New business unit» header, no tabs', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('New business unit');
    expect(root.querySelector('mat-tab-group')).toBeNull();
    expect(root.querySelector('nx-business-unit-form')).toBeTruthy();
  });

  describe('audit tab', () => {
    it('shows an "Auditoría" tab for an existing record and renders nx-audit-trail when selected', async () => {
      const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub(businessUnit));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const audit = await selectAuditTab(fixture);

      expect(audit).not.toBeNull();
      expect(audit?.aggregateType()).toBe('business_unit');
      expect(audit?.aggregateId()).toBe(businessUnit.id);
    });

    it('has no "Auditoría" tab when creating a new record', async () => {
      const fixture = await setupPage({}, createFacadeStub(null));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(findAuditTab(fixture)).toBeUndefined();
    });

    it('has no "Auditoría" tab without AUDIT:READ', async () => {
      const fixture = await setupPage({ id: businessUnit.id }, createFacadeStub(businessUnit));
      denyAuditRead();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(findAuditTab(fixture)).toBeUndefined();
    });
  });
});
