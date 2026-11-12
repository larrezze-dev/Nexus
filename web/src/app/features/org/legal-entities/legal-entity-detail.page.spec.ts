import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { LegalEntitiesFacade } from './legal-entities.facade';
import { LegalEntityDetailPage } from './legal-entity-detail.page';
import { denyAuditRead, findAuditTab, selectAuditTab, stubAuditTrail } from '../audit-tab.testing';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { Party } from '../../../api/platform/models/party';

const legalEntity: LegalEntity = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c11',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const party: Party = {
  id: legalEntity.partyId,
  partyType: 'ORGANIZATION',
  legalName: 'SAPE smart bud AG',
  displayName: 'SSB Schweiz',
  taxId: null,
  erpCompanyCode: null,
  isInternal: true,
  isActive: true,
  createdAt: legalEntity.createdAt,
  updatedAt: legalEntity.updatedAt,
};

const translations = {
  en: {
    org: {
      legalEntities: {
        newTitle: 'New legal entity',
        loading: 'Loading…',
        deactivate: 'Deactivate',
        deactivateDialog: { title: 'Deactivate?', message: '{{name}} ({{code}}) will be deactivated.' },
        deactivated: 'Deactivated',
        detail: { modified: 'modified on {{date}}' },
        fields: {},
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
  },
};

function createFacadeStub(initialSelected: LegalEntity | null, initialParty: Party | null = initialSelected ? party : null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    party: signal(initialParty).asReadonly(),
    loading: signal(false).asReadonly(),
    error: signal<string | null>(null).asReadonly(),
    groupOptions: vi.fn().mockResolvedValue([{ id: legalEntity.groupId, code: 'SAPE', name: 'SAPE Holding AG' }]),
    currencyOptions: vi.fn().mockResolvedValue([]),
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
      LegalEntityDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: LegalEntitiesFacade, useValue: facade },
      { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null | undefined) => code ?? 'generic' } },
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

  stubAuditTrail(LegalEntityDetailPage);
  const fixture: ComponentFixture<LegalEntityDetailPage> = TestBed.createComponent(LegalEntityDetailPage);
  return fixture;
}

describe('LegalEntityDetailPage', () => {
  it('loads the entity in edit mode when the route has an id', async () => {
    const facade = createFacadeStub(legalEntity);
    const fixture = await setupPage({ id: legalEntity.id }, facade);
    fixture.detectChanges();

    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(legalEntity.id);
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

    await fixture.componentInstance['onSave']({
      legalName: 'Nexus Event AG',
      displayName: '',
      groupId: legalEntity.groupId,
      code: 'LE-CH',
      currencyCode: 'CHF',
      countryCode: 'CH',
    });

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['Code already in use'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
  });

  it('translates a 422 business-rule code via NxI18nService', async () => {
    const facade = createFacadeStub(legalEntity);
    facade.update.mockRejectedValue({ status: 422, error: { code: 'ORG-R7' } });
    const fixture = await setupPage({ id: legalEntity.id }, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave']({
      legalName: '',
      displayName: '',
      groupId: '',
      code: 'LE-CH',
      currencyCode: 'CHF',
      countryCode: 'CH',
    });

    expect(fixture.componentInstance['saveError']()).toBe('ORG-R7');
  });

  it('navigates to the created entity after a successful create', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockResolvedValue(legalEntity);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    const router = TestBed.inject(Router);

    await fixture.componentInstance['onSave']({
      legalName: 'Nexus Event AG',
      displayName: '',
      groupId: legalEntity.groupId,
      code: 'LE-CH',
      currencyCode: 'CHF',
      countryCode: 'CH',
    });

    expect(router.navigate).toHaveBeenCalledWith(['..', legalEntity.id], { relativeTo: expect.anything() });
  });

  it('shows the legal name as title, code · last change as subtitle and the status in the header', async () => {
    const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub(legalEntity));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('h1').length).toBe(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('SAPE smart bud AG');
    expect(root.querySelector('.nx-page-header__subtitle')?.textContent).toContain('LE-CH · modified on');
    expect(root.querySelector('nx-page-header nx-status-badge')).toBeTruthy();
    expect(root.querySelector('nx-page')?.classList).toContain('nx-page--form');
  });

  it('names the record in the breadcrumb with its legal name once loaded', async () => {
    const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub(legalEntity));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(TestBed.inject(NxBreadcrumbService).setCurrentLabel).toHaveBeenCalledWith('SAPE smart bud AG');
  });

  it('deactivates only after the destructive confirmation dialog, naming the record', async () => {
    const facade = createFacadeStub(legalEntity);
    facade.deactivate.mockResolvedValue({ ...legalEntity, isActive: false });
    const fixture = await setupPage({ id: legalEntity.id }, facade);
    fixture.detectChanges();

    confirmStub.confirm.mockResolvedValue(false);
    await fixture.componentInstance['onDeactivate']();
    expect(confirmStub.confirm).toHaveBeenCalledWith(
      expect.objectContaining({ destructive: true, message: 'SAPE smart bud AG (LE-CH) will be deactivated.' }),
    );
    expect(facade.deactivate).not.toHaveBeenCalled();

    confirmStub.confirm.mockResolvedValue(true);
    await fixture.componentInstance['onDeactivate']();
    expect(facade.deactivate).toHaveBeenCalledWith(legalEntity.id);
  });

  it('offers no Deactivate on an inactive entity (there is no reactivate in the API)', async () => {
    const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub({ ...legalEntity, isActive: false }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="le-detail-deactivate"]')).toBeNull();
  });

  it('is read-only with a notice and no Deactivate without ORG_STRUCTURE:UPDATE', async () => {
    const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub(legalEntity));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:READ', scopes: [] }]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="le-detail-read-only"]')).toBeTruthy();
    expect(root.querySelector('[data-testid="le-detail-deactivate"]')).toBeNull();
    expect((root.querySelector('[data-testid="le-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('uses the create template: «New legal entity» header, no tabs', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('New legal entity');
    expect(root.querySelector('mat-tab-group')).toBeNull();
    expect(root.querySelector('nx-legal-entity-form')).toBeTruthy();
  });

  describe('audit tab', () => {
    it('shows an "Auditoría" tab for an existing record and renders nx-audit-trail when selected', async () => {
      const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub(legalEntity));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const audit = await selectAuditTab(fixture);

      expect(audit).not.toBeNull();
      expect(audit?.aggregateType()).toBe('legal-entity');
      expect(audit?.aggregateId()).toBe(legalEntity.id);
    });

    it('has no "Auditoría" tab when creating a new record', async () => {
      const fixture = await setupPage({}, createFacadeStub(null));
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      expect(findAuditTab(fixture)).toBeUndefined();
    });

    it('has no "Auditoría" tab without AUDIT:READ', async () => {
      const fixture = await setupPage({ id: legalEntity.id }, createFacadeStub(legalEntity));
      denyAuditRead();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(findAuditTab(fixture)).toBeUndefined();
    });
  });
});
