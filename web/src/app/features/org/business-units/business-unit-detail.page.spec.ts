import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { BusinessUnit } from '../../../api/platform/models/business-unit';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { LegalEntitiesFacade } from '../legal-entities/legal-entities.facade';
import { BusinessUnitsFacade } from './business-units.facade';
import { BusinessUnitDetailPage } from './business-unit-detail.page';

const legalEntity: LegalEntity = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c11',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

const businessUnit: BusinessUnit = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c12',
  legalEntityId: legalEntity.id,
  code: 'BU-01',
  captionI18n: { de: 'Vermietung', en: 'Rental' },
  costCenterCode: 'CC-100',
  isActive: true,
};

const translations = {
  en: {
    org: {
      businessUnits: {
        newTitle: 'New business unit',
        loading: 'Loading…',
        deactivate: 'Deactivate',
        confirmDeactivate: 'Are you sure?',
        fields: {},
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
    shared: {
      entityPicker: { placeholder: 'Search…', noResults: 'No results' },
      i18nCaptionEditor: {
        groupLabel: 'Caption',
        deRequired: 'German is required',
        language: { de: 'German', en: 'English', fr: 'French', it: 'Italian', es: 'Spanish' },
      },
    },
  },
};

function createBusinessUnitFacadeStub(initialSelected: BusinessUnit | null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    loading: signal(false).asReadonly(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
  };
}

function createLegalEntitiesFacadeStub(selected: LegalEntity | null) {
  return {
    selected: signal(selected).asReadonly(),
    load: vi.fn().mockResolvedValue(undefined),
    search: vi.fn().mockReturnValue({ subscribe: () => {} }),
  };
}

async function setupPage(
  paramMap: Record<string, string>,
  businessUnitsFacade: ReturnType<typeof createBusinessUnitFacadeStub>,
  legalEntitiesFacade: ReturnType<typeof createLegalEntitiesFacadeStub>,
) {
  await TestBed.configureTestingModule({
    imports: [
      BusinessUnitDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: BusinessUnitsFacade, useValue: businessUnitsFacade },
      { provide: LegalEntitiesFacade, useValue: legalEntitiesFacade },
      { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null | undefined) => code ?? 'generic' } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
    ],
  }).compileComponents();

  const fixture: ComponentFixture<BusinessUnitDetailPage> = TestBed.createComponent(BusinessUnitDetailPage);
  return fixture;
}

describe('BusinessUnitDetailPage', () => {
  it('loads the business unit and its legal entity (for the read-only display) in edit mode', async () => {
    const buFacade = createBusinessUnitFacadeStub(businessUnit);
    const leFacade = createLegalEntitiesFacadeStub(legalEntity);
    const fixture = await setupPage({ id: businessUnit.id }, buFacade, leFacade);
    fixture.detectChanges();
    await fixture.componentInstance['initialized'];

    expect(buFacade.reset).toHaveBeenCalled();
    expect(buFacade.load).toHaveBeenCalledWith(businessUnit.id);
    expect(leFacade.load).toHaveBeenCalledWith(legalEntity.id);
    expect(fixture.componentInstance['legalEntityDisplay']()).toBe('LE-CH');
  });

  it('does not load anything in create mode', async () => {
    const buFacade = createBusinessUnitFacadeStub(null);
    const leFacade = createLegalEntitiesFacadeStub(null);
    const fixture = await setupPage({}, buFacade, leFacade);
    fixture.detectChanges();

    expect(buFacade.load).not.toHaveBeenCalled();
    expect(leFacade.load).not.toHaveBeenCalled();
  });

  it('maps a 400 response into per-field errors', async () => {
    const buFacade = createBusinessUnitFacadeStub(null);
    buFacade.create.mockRejectedValue({ status: 400, error: { errors: { code: ['Code already in use'] } } });
    const leFacade = createLegalEntitiesFacadeStub(null);
    const fixture = await setupPage({}, buFacade, leFacade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave']({
      legalName: 'Vermietung CH',
      displayName: '',
      legalEntityId: legalEntity.id,
      code: 'BU-01',
      captionI18n: { de: 'Vermietung' },
      costCenterCode: '',
    });

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ code: ['Code already in use'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
  });

  it('navigates to the created entity after a successful create', async () => {
    const buFacade = createBusinessUnitFacadeStub(null);
    buFacade.create.mockResolvedValue(businessUnit);
    const leFacade = createLegalEntitiesFacadeStub(null);
    const fixture = await setupPage({}, buFacade, leFacade);
    fixture.detectChanges();
    const router = TestBed.inject(Router);

    await fixture.componentInstance['onSave']({
      legalName: 'Vermietung CH',
      displayName: '',
      legalEntityId: legalEntity.id,
      code: 'BU-01',
      captionI18n: { de: 'Vermietung' },
      costCenterCode: '',
    });

    expect(router.navigate).toHaveBeenCalledWith(['..', businessUnit.id], { relativeTo: expect.anything() });
  });
});
