import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { describe, expect, it, vi } from 'vitest';
import type { Party } from '../../../api/platform/models/party';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { NxConfirm } from '../../../shared/ui/layout';
import { PartiesFacade } from './parties.facade';
import { PartyDetailPage } from './party-detail.page';
import type { PartyFormValue } from './party-form.component';

const party: Party = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c20',
  partyType: 'ORGANIZATION',
  legalName: 'Acme Events SA',
  displayName: 'Acme Events',
  taxId: null,
  erpCompanyCode: null,
  isInternal: false,
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const formValue: PartyFormValue = {
  partyType: 'PERSON',
  isInternal: false,
  legalName: 'Jane Doe',
  displayName: '',
  taxId: '',
  erpCompanyCode: '',
};

const translations = {
  en: {
    org: {
      parties: {
        title: 'Parties',
        newTitle: 'New party',
        loading: 'Loading…',
        deactivate: 'Deactivate',
        deactivateDialog: { title: 'Deactivate?', message: '{{name}} will be deactivated.' },
        deactivated: 'Deactivated',
        detail: { modified: 'modified on {{date}}' },
        type: { ORGANIZATION: 'Organization', PERSON: 'Person' },
        scope: { internal: 'Internal', external: 'External' },
        fields: {},
      },
    },
    errors: { validation: 'Fix the fields', generic: 'Something went wrong' },
    common: { save: 'Save' },
  },
};

function createFacadeStub(initialSelected: Party | null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    loading: signal(false).asReadonly(),
    error: signal<string | null>(null).asReadonly(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
    deactivate: vi.fn(),
  };
}

const confirmStub = { confirm: vi.fn() };

async function setupPage(
  paramMap: Record<string, string>,
  facade: ReturnType<typeof createFacadeStub>,
): Promise<ComponentFixture<PartyDetailPage>> {
  confirmStub.confirm.mockReset();
  await TestBed.configureTestingModule({
    imports: [
      PartyDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: PartiesFacade, useValue: facade },
      { provide: NxI18nService, useValue: { translateErrorCode: (code: string | null | undefined) => code ?? 'generic' } },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
      {
        provide: NxBreadcrumbService,
        useValue: { setCurrentLabel: vi.fn(), claimForPage: () => () => undefined, trail: signal([]) },
      },
      { provide: NxConfirm, useValue: confirmStub },
    ],
  }).compileComponents();

  return TestBed.createComponent(PartyDetailPage);
}

type PageInternals = {
  onSave(value: PartyFormValue): Promise<void>;
  onDeactivate(): Promise<void>;
  fieldErrors(): Record<string, readonly string[]>;
  saveError(): string | null;
};

function internals(fixture: ComponentFixture<PartyDetailPage>): PageInternals {
  return fixture.componentInstance as unknown as PageInternals;
}

describe('PartyDetailPage', () => {
  it('loads the party in edit mode when the route has an id', async () => {
    const facade = createFacadeStub(party);
    const fixture = await setupPage({ id: party.id }, facade);
    fixture.detectChanges();

    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(party.id);
  });

  it('uses the create template: «New party» header, the form, no tabs and no load', async () => {
    const facade = createFacadeStub(null);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(facade.load).not.toHaveBeenCalled();
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('New party');
    expect(root.querySelector('mat-tab-group')).toBeNull();
    expect(root.querySelector('nx-party-form')).toBeTruthy();
  });

  it('creates with nulls for empty optional fields and opens the new party', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockResolvedValue({ ...party, id: 'new-id' });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    await internals(fixture).onSave(formValue);

    expect(facade.create).toHaveBeenCalledWith({
      partyType: 'PERSON',
      isInternal: false,
      legalName: 'Jane Doe',
      displayName: null,
      taxId: null,
      erpCompanyCode: null,
    });
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['..', 'new-id'], { relativeTo: expect.anything() });
  });

  it('updates without type and scope (immutable, ORG-R2)', async () => {
    const facade = createFacadeStub(party);
    facade.update.mockResolvedValue(party);
    const fixture = await setupPage({ id: party.id }, facade);
    fixture.detectChanges();

    await internals(fixture).onSave({ ...formValue, legalName: 'Acme Events SA', taxId: 'CHE-123.456.789' });

    expect(facade.update).toHaveBeenCalledWith(party.id, {
      legalName: 'Acme Events SA',
      displayName: null,
      taxId: 'CHE-123.456.789',
      erpCompanyCode: null,
    });
  });

  it('maps a 400 response into per-field errors and a validation message', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockRejectedValue({ status: 400, error: { errors: { legalName: ['Too long'] } } });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    await internals(fixture).onSave(formValue);

    expect(internals(fixture).fieldErrors()).toEqual({ legalName: ['Too long'] });
    expect(internals(fixture).saveError()).toBe('Fix the fields');
  });

  it('translates a 422 business-rule code via NxI18nService', async () => {
    const facade = createFacadeStub(party);
    facade.update.mockRejectedValue({ status: 422, error: { code: 'ORG-R2' } });
    const fixture = await setupPage({ id: party.id }, facade);
    fixture.detectChanges();

    await internals(fixture).onSave(formValue);

    expect(internals(fixture).saveError()).toBe('ORG-R2');
  });

  it('shows the legal name as title, type · scope · last change as subtitle and the status', async () => {
    const fixture = await setupPage({ id: party.id }, createFacadeStub(party));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelectorAll('h1').length).toBe(1);
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('Acme Events SA');
    expect(root.querySelector('.nx-page-header__subtitle')?.textContent).toContain('Organization · External · modified on');
    expect(root.querySelector('nx-page-header nx-status-badge')).toBeTruthy();
    expect(root.querySelector('nx-page')?.classList).toContain('nx-page--form');
    // No audit projection for parties: no tabs at all.
    expect(root.querySelector('mat-tab-group')).toBeNull();
  });

  it('names the record in the breadcrumb with its legal name once loaded', async () => {
    const fixture = await setupPage({ id: party.id }, createFacadeStub(party));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(TestBed.inject(NxBreadcrumbService).setCurrentLabel).toHaveBeenCalledWith('Acme Events SA');
  });

  it('deactivates only after the destructive confirmation dialog, naming the record', async () => {
    const facade = createFacadeStub(party);
    facade.deactivate.mockResolvedValue({ ...party, isActive: false });
    const fixture = await setupPage({ id: party.id }, facade);
    fixture.detectChanges();

    confirmStub.confirm.mockResolvedValue(false);
    await internals(fixture).onDeactivate();
    expect(confirmStub.confirm).toHaveBeenCalledWith(
      expect.objectContaining({ destructive: true, message: 'Acme Events SA will be deactivated.' }),
    );
    expect(facade.deactivate).not.toHaveBeenCalled();

    confirmStub.confirm.mockResolvedValue(true);
    await internals(fixture).onDeactivate();
    expect(facade.deactivate).toHaveBeenCalledWith(party.id);
  });

  it('offers Deactivate with PARTY:UPDATE, none on an inactive party (no reactivate in the API)', async () => {
    const fixture = await setupPage({ id: party.id }, createFacadeStub(party));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'PARTY:UPDATE', scopes: [] } as never]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="party-detail-deactivate"]')).toBeTruthy();

    TestBed.resetTestingModule();
    const inactive = await setupPage({ id: party.id }, createFacadeStub({ ...party, isActive: false }));
    inactive.detectChanges();
    expect((inactive.nativeElement as HTMLElement).querySelector('[data-testid="party-detail-deactivate"]')).toBeNull();
  });

  it('is read-only with a notice and no Deactivate without PARTY:UPDATE', async () => {
    const fixture = await setupPage({ id: party.id }, createFacadeStub(party));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'PARTY:READ', scopes: [] } as never]);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[data-testid="party-detail-read-only"]')).toBeTruthy();
    expect(root.querySelector('[data-testid="party-detail-deactivate"]')).toBeNull();
    expect((root.querySelector('[data-testid="party-form-submit"]') as HTMLButtonElement).disabled).toBe(true);
  });
});
