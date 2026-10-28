import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { signal } from '@angular/core';
import { UserDetailPage } from './user-detail.page';
import { SYSTEM_USER_ID, UsersFacade } from './users.facade';

function setup(paramMap: Record<string, string>, facadeOverrides: Partial<Record<string, unknown>> = {}) {
  const facade = {
    selected: signal(null),
    loading: signal(false),
    saving: signal(false),
    error: signal(null),
    fieldErrors: signal(null),
    load: vi.fn().mockResolvedValue(undefined),
    create: vi.fn().mockResolvedValue(null),
    update: vi.fn().mockResolvedValue(null),
    deactivate: vi.fn().mockResolvedValue(null),
    reset: vi.fn(),
    searchBusinessUnits: vi.fn(),
    formatBusinessUnit: (bu: { code: string; captionI18n: { de: string } }) => `${bu.code} — ${bu.captionI18n.de}`,
    describeBusinessUnit: vi.fn().mockResolvedValue('BU-01 — Operations'),
    ...facadeOverrides,
  };

  TestBed.configureTestingModule({
    imports: [UserDetailPage],
    providers: [
      provideNoopAnimations(),
      ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
      { provide: UsersFacade, useValue: facade },
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap(paramMap) } },
      },
    ],
  });

  const fixture = TestBed.createComponent(UserDetailPage);
  return { fixture, facade };
}

describe('UserDetailPage', () => {
  it('is in "new" mode without an id param and does not call load()', async () => {
    const { fixture, facade } = setup({});
    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.load).not.toHaveBeenCalled();
    expect(facade.reset).toHaveBeenCalled();
  });

  it('loads an existing user and populates the form', async () => {
    const user = {
      id: 'u-1',
      fullName: 'Jane Doe',
      email: 'jane@example.com',
      oid: 'oid-1',
      preferredLanguage: 'en' as const,
      businessUnitId: null,
      partyId: 'p-1',
      isActive: true,
    };
    const { fixture, facade } = setup(
      { id: 'u-1' },
      { selected: signal(user), load: vi.fn().mockImplementation(async () => {}) },
    );
    // `load()` is a no-op mock, so manually point `selected` before `ngOnInit` reads it.
    (facade.load as ReturnType<typeof vi.fn>).mockImplementation(async () => undefined);

    fixture.detectChanges();
    await fixture.whenStable();

    expect(facade.load).toHaveBeenCalledWith('u-1');
    expect(fixture.componentInstance['form'].controls.fullName.value).toBe('Jane Doe');
  });

  it('hides the deactivate action and disables the form for the SYSTEM user (ORG-R6)', async () => {
    const systemUser = {
      id: SYSTEM_USER_ID,
      fullName: 'SYSTEM',
      email: null,
      oid: null,
      preferredLanguage: 'de' as const,
      businessUnitId: null,
      partyId: 'p-system',
      isActive: true,
    };
    const { fixture } = setup(
      { id: SYSTEM_USER_ID },
      { selected: signal(systemUser) },
    );

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance['isSystemUser']()).toBe(true);
    expect(fixture.componentInstance['form'].disabled).toBe(true);
    const deactivateButton = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="user-deactivate-button"]',
    );
    expect(deactivateButton).toBeNull();
  });

  it('shows the ORG-R9 email field error returned by the facade', async () => {
    const { fixture, facade } = setup({}, { fieldErrors: signal({ email: 'A user with this email already exists.' }) });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const error = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="user-email-error"]');
    expect(error?.textContent).toContain('already exists');
    expect(facade.create).not.toHaveBeenCalled();
  });
});
