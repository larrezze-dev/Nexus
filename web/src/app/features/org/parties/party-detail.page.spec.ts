import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { PartyDetailPage } from './party-detail.page';
import { PartiesFacade } from './parties.facade';

function setup(paramMap: Record<string, string>, facadeOverrides: Partial<Record<string, unknown>> = {}) {
  const facade = {
    selected: signal(null),
    loading: signal(false),
    saving: signal(false),
    error: signal(null),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn().mockResolvedValue(null),
    update: vi.fn().mockResolvedValue(null),
    deactivate: vi.fn().mockResolvedValue(null),
    reset: vi.fn(),
    ...facadeOverrides,
  };

  TestBed.configureTestingModule({
    imports: [PartyDetailPage],
    providers: [
      provideNoopAnimations(),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: PartiesFacade, useValue: facade },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
    ],
  });

  const fixture = TestBed.createComponent(PartyDetailPage);
  return { fixture, facade };
}

describe('PartyDetailPage', () => {
  it('defaults to "new" mode and does not call load()', async () => {
    const { fixture, facade } = setup({});
    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.load).not.toHaveBeenCalled();
    expect(facade.reset).toHaveBeenCalled();
    // Manual alta from this directory defaults to an EXTERNAL party.
    expect(fixture.componentInstance['form'].controls.isInternal.value).toBe(false);
  });

  it('loads an existing party and disables partyType/isInternal (ORG-R2: immutable after creation)', async () => {
    const party = {
      id: 'p-1',
      partyType: 'ORGANIZATION' as const,
      legalName: 'Acme Events SA',
      displayName: 'Acme Events',
      taxId: null,
      erpCompanyCode: null,
      isInternal: false,
      isActive: true,
    };
    const { fixture } = setup({ id: 'p-1' }, { selected: signal(party) });

    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance['form'].controls.legalName.value).toBe('Acme Events SA');
    expect(fixture.componentInstance['form'].controls.partyType.disabled).toBe(true);
    expect(fixture.componentInstance['form'].controls.isInternal.disabled).toBe(true);
  });

  it('calls create() with the form values on submit when in "new" mode', async () => {
    const { fixture, facade } = setup({});
    fixture.detectChanges();
    await fixture.whenStable();

    fixture.componentInstance['form'].setValue({
      partyType: 'PERSON',
      legalName: 'Jane Doe',
      displayName: '',
      taxId: '',
      erpCompanyCode: '',
      isInternal: false,
    });

    await fixture.componentInstance['save']();

    expect(facade.create).toHaveBeenCalledWith({
      partyType: 'PERSON',
      legalName: 'Jane Doe',
      displayName: null,
      taxId: null,
      erpCompanyCode: null,
      isInternal: false,
    });
  });
});
