import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { LegalEntity } from '../../../api/platform/models/legal-entity';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { LegalEntitiesFacade } from './legal-entities.facade';
import { LegalEntityDetailPage } from './legal-entity-detail.page';

const legalEntity: LegalEntity = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  partyId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c11',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

const translations = {
  en: {
    org: {
      legalEntities: {
        newTitle: 'New legal entity',
        loading: 'Loading…',
        deactivate: 'Deactivate',
        confirmDeactivate: 'Are you sure?',
        fields: {},
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
  },
};

function createFacadeStub(initialSelected: LegalEntity | null) {
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

async function setupPage(paramMap: Record<string, string>, facade: ReturnType<typeof createFacadeStub>) {
  await TestBed.configureTestingModule({
    imports: [
      LegalEntityDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
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
    ],
  }).compileComponents();

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
});
