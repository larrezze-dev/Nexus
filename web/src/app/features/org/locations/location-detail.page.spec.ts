import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Location } from '../../../api/platform/models/location';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { PermissionsStore } from '../../../shared/auth/permissions.store';
import { LocationDetailPage } from './location-detail.page';
import { LocationFormValue } from './location-form.component';
import { LocationFacade } from './location.facade';

const location: Location = {
  id: '00000000-0000-7000-8000-0000000b0501',
  code: 'LOC-CH-01',
  name: 'Lager Zug',
  street: null,
  postalCode: '6300',
  city: 'Zug',
  region: null,
  countryCode: 'CH',
  isActive: true,
  createdAt: '2026-10-18T16:20:00Z',
  updatedAt: '2026-10-18T16:20:00Z',
};

const formValue: LocationFormValue = {
  name: 'Lager Baar',
  code: null,
  street: 'Industriestrasse 1',
  postalCode: '6340',
  city: 'Baar',
  region: null,
  countryCode: 'CH',
};

const locations = {
  title: 'Locations',
  newTitle: 'New location',
  loading: 'Loading…',
  created: 'Created',
  saved: 'Saved',
  detail: { modified: 'modified on {{date}}' },
};

const translations = {
  en: { org: { locations }, errors: { validation: 'Fix the fields', generic: 'Something went wrong' } },
};

function createFacadeStub(initialSelected: Location | null, initialError: string | null = null) {
  return {
    selected: signal(initialSelected).asReadonly(),
    loading: signal(false).asReadonly(),
    error: signal<string | null>(initialError).asReadonly(),
    reset: vi.fn(),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn(),
    update: vi.fn(),
  };
}

async function setupPage(paramMap: Record<string, string>, facade: ReturnType<typeof createFacadeStub>) {
  await TestBed.configureTestingModule({
    imports: [
      LocationDetailPage,
      TranslocoTestingModule.forRoot({
        langs: translations,
        translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        preloadLangs: true,
      }),
    ],
    providers: [
      provideNoopAnimations(),
      { provide: LocationFacade, useValue: facade },
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } } },
      { provide: Router, useValue: { navigate: vi.fn().mockResolvedValue(true) } },
      {
        provide: NxBreadcrumbService,
        useValue: { setCurrentLabel: vi.fn(), claimForPage: () => () => undefined, trail: signal([]) },
      },
    ],
  }).compileComponents();

  vi.spyOn(TestBed.inject(NxI18nService), 'translateErrorCode').mockImplementation((code) => code ?? 'generic');
  const fixture: ComponentFixture<LocationDetailPage> = TestBed.createComponent(LocationDetailPage);
  return fixture;
}

function root(fixture: ComponentFixture<LocationDetailPage>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

describe('LocationDetailPage', () => {
  it('loads the location in edit mode when the route has an id', async () => {
    const facade = createFacadeStub(location);
    const fixture = await setupPage({ id: location.id }, facade);
    fixture.detectChanges();

    expect(facade.reset).toHaveBeenCalled();
    expect(facade.load).toHaveBeenCalledWith(location.id);
  });

  it('uses the create template: «New location» header, no tabs, nothing loaded', async () => {
    const facade = createFacadeStub(null);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    expect(facade.load).not.toHaveBeenCalled();
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('New location');
    expect(root(fixture).querySelector('mat-tab-group')).toBeNull();
    expect(root(fixture).querySelector('nx-location-form')).toBeTruthy();
  });

  it('shows the name as title, code · last change as subtitle, the status and no header action', async () => {
    const fixture = await setupPage({ id: location.id }, createFacadeStub(location));
    fixture.detectChanges();

    expect(root(fixture).querySelectorAll('h1').length).toBe(1);
    expect(root(fixture).querySelector('h1')?.textContent?.trim()).toBe('Lager Zug');
    expect(root(fixture).querySelector('.nx-page-header__subtitle')?.textContent).toContain(
      'LOC-CH-01 · modified on',
    );
    expect(root(fixture).querySelector('nx-page-header nx-status-badge')).toBeTruthy();
    // The API cannot deactivate a location.
    expect(root(fixture).querySelector('.nx-page-header__actions button')).toBeNull();
    expect(root(fixture).querySelector('nx-page')?.classList).toContain('nx-page--form');
  });

  it('shows only the date in the subtitle when the location has no code', async () => {
    const fixture = await setupPage({ id: location.id }, createFacadeStub({ ...location, code: null }));
    fixture.detectChanges();

    const subtitle = root(fixture).querySelector('.nx-page-header__subtitle')?.textContent?.trim() ?? '';
    expect(subtitle.startsWith('modified on')).toBe(true);
  });

  it('has a General tab only (no audit projection for locations)', async () => {
    const fixture = await setupPage({ id: location.id }, createFacadeStub(location));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const tabs = Array.from(root(fixture).querySelectorAll('[role="tab"]')).map((tab) => tab.textContent?.trim());
    expect(tabs).toEqual(['common.tabs.general']);
  });

  it('names the location in the breadcrumb', async () => {
    const fixture = await setupPage({ id: location.id }, createFacadeStub(location));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(TestBed.inject(NxBreadcrumbService).setCurrentLabel).toHaveBeenLastCalledWith('Lager Zug');
  });

  it('shows the error state with «Retry» when the load fails', async () => {
    const facade = createFacadeStub(null, 'load_failed');
    const fixture = await setupPage({ id: location.id }, facade);
    fixture.detectChanges();

    const errorState = root(fixture).querySelector('[data-testid="location-detail-load-error"]');
    expect(errorState).toBeTruthy();
    (errorState?.querySelector('button') as HTMLButtonElement).click();
    expect(facade.load).toHaveBeenCalledTimes(2);
  });

  it('is read-only with a notice without ORG_STRUCTURE:UPDATE', async () => {
    const fixture = await setupPage({ id: location.id }, createFacadeStub(location));
    TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:READ', scopes: [] }]);
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-testid="location-detail-read-only"]')).toBeTruthy();
    expect((root(fixture).querySelector('[data-testid="location-form-submit"]') as HTMLButtonElement).disabled).toBe(
      true,
    );
  });

  it('creates the location and opens it', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockResolvedValue(location);
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(facade.create).toHaveBeenCalledWith(formValue);
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/locations', location.id]);
  });

  it('updates the location with every field', async () => {
    const facade = createFacadeStub(location);
    facade.update.mockResolvedValue(location);
    const fixture = await setupPage({ id: location.id }, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(facade.update).toHaveBeenCalledWith(location.id, formValue);
  });

  it('maps a 400 response into per-field errors and a validation message', async () => {
    const facade = createFacadeStub(null);
    facade.create.mockRejectedValue({ status: 400, error: { errors: { countryCode: ['Unknown country'] } } });
    const fixture = await setupPage({}, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(fixture.componentInstance['fieldErrors']()).toEqual({ countryCode: ['Unknown country'] });
    expect(fixture.componentInstance['saveError']()).toBe('Fix the fields');
  });

  it('translates a 422 business-rule code via NxI18nService', async () => {
    const facade = createFacadeStub(location);
    facade.update.mockRejectedValue({ status: 422, error: { code: 'ORG-R9' } });
    const fixture = await setupPage({ id: location.id }, facade);
    fixture.detectChanges();

    await fixture.componentInstance['onSave'](formValue);

    expect(fixture.componentInstance['saveError']()).toBe('ORG-R9');
  });

  it('cancel goes back to the list', async () => {
    const fixture = await setupPage({}, createFacadeStub(null));
    fixture.detectChanges();

    fixture.componentInstance['onCancel']();

    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/locations']);
  });
});
