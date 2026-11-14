import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import type { ValueGetterParams } from 'ag-grid-community';
import { of } from 'rxjs';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { UserListPage } from './user-list.page';
import { UsersFacade } from './users.facade';

describe('UserListPage', () => {
  const activeLang = signal('es');

  function setup() {
    const list = vi.fn(() => of({ items: [], page: 0, pageSize: 25, total: 0 }));
    const facade = {
      list,
      businessUnits: () => of([{ id: 'bu-1', code: 'BU-1', captionI18n: { de: 'Montage', es: 'Montaje' } }]),
      languageName: (code: string) => ({ de: activeLang() === 'es' ? 'alemán' : 'Deutsch' })[code] ?? code,
    };
    TestBed.configureTestingModule({
      imports: [UserListPage],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        { provide: UsersFacade, useValue: facade },
        { provide: NxI18nService, useValue: { activeLang } },
      ],
    });
    const fixture = TestBed.createComponent(UserListPage);
    fixture.detectChanges();
    return { fixture, list };
  }

  it('fetches through the facade (FE-R1: never HttpClient directly)', () => {
    const { list } = setup();
    expect(list).toHaveBeenCalled();
  });

  it('columns have translatable headers, show the language by name and the business unit by caption', () => {
    const { fixture } = setup();
    const columns = fixture.componentInstance['columns']();
    const row = { fullName: 'Jane', preferredLanguage: 'de', businessUnitId: 'bu-1', isActive: true } as UserListItem;
    const value = (colId: string, data: UserListItem) =>
      (columns.find((c) => c.colId === colId)?.valueGetter as (p: ValueGetterParams<UserListItem>) => unknown)({
        data,
      } as ValueGetterParams<UserListItem>);

    expect(columns.every((c) => !!c.headerKey)).toBe(true);
    expect(value('preferredLanguage', row)).toBe('alemán');
    expect(value('businessUnit', row)).toBe('Montaje');
    expect(value('businessUnit', { ...row, businessUnitId: null })).toBe('—');
  });

  it('list template: header with breadcrumb, count after the load, cards with name/email/status', async () => {
    const { fixture, list } = setup();
    list.mockReturnValue(of({ items: [], page: 0, pageSize: 25, total: 5 }));
    fixture.componentInstance['fetchFn'](0, 25, null, {}).subscribe();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('nx-page-header h1')?.textContent).toContain('org.users.title');
    expect(root.querySelector('[data-testid="nx-page-header-count"]')).not.toBeNull();
    expect(fixture.componentInstance['cardFields']).toEqual({ title: 'fullName', subtitle: 'email', status: 'isActive' });
  });
});
