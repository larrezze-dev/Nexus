import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTransloco } from '@jsverse/transloco';
import { AccountPage } from './account.page';
import { AccountFacade } from './account.facade';
import { Me } from '../../api/platform/models/me';

describe('AccountPage', () => {
  it('renders every /v1/me field once the facade has loaded', async () => {
    const me: Me = {
      subject: 'sub-1',
      oid: 'oid-1',
      displayName: 'Jane Doe',
      email: 'jane@example.com',
      userId: null,
      partyId: null,
      businessUnitId: null,
      preferredLanguage: 'es',
      provisioned: true,
      permissions: ['ASSET_TYPE:READ', 'ASSET_TYPE:CREATE'],
    };
    const load = vi.fn();

    TestBed.configureTestingModule({
      imports: [AccountPage],
      providers: [
        ...provideTransloco({
          config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' },
        }),
        {
          provide: AccountFacade,
          useValue: {
            load,
            me: signal(me),
            loading: signal(false),
            error: signal(null),
          },
        },
      ],
    });

    const fixture = TestBed.createComponent(AccountPage);
    fixture.detectChanges();

    expect(load).toHaveBeenCalledOnce();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Jane Doe');
    expect(text).toContain('jane@example.com');
    expect(text).toContain('es');
    expect(text).toContain('ASSET_TYPE:READ, ASSET_TYPE:CREATE');
  });
});
