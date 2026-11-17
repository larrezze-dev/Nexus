import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { MockInstance } from 'vitest';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { RoleListItem } from '../../../api/platform/models/role-list-item';
import { Page } from '../../../shared/ui/page.model';
import { RoleListPage } from './role-list.page';
import { RolesFacade } from './roles.facade';

const listItem: RoleListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'ADMIN',
  captionI18n: { de: 'Administrator' },
  isSystem: false,
  isActive: true,
};

describe('RoleListPage', () => {
  let fixture: ComponentFixture<RoleListPage>;
  let navigateSpy: MockInstance<Router['navigate']>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        RoleListPage,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              access: { roles: { title: 'Roles', new: 'New', fields: {}, empty: 'No rows' } },
              common: { yes: 'Yes', no: 'No' },
              shared: { dataGrid: { empty: 'No rows' } },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        provideRouter([]),
        {
          provide: RolesFacade,
          useValue: { list: () => of<Page<RoleListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }) },
        },
      ],
    }).compileComponents();
    navigateSpy = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(RoleListPage);
  });

  it('opens the detail route of a row (click, Enter or card tap)', () => {
    fixture.detectChanges();

    fixture.componentInstance['open'](listItem);

    expect(navigateSpy).toHaveBeenCalledWith([listItem.id], { relativeTo: expect.anything() });
  });

  it('uses the list template: page header with the title and the record count', async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('nx-page-header h1')?.textContent?.trim()).toBe('Roles');
    expect(fixture.componentInstance['total']()).toBe(1);
  });

  it('shows the role name and code on the mobile cards', () => {
    expect(fixture.componentInstance['cardFields']).toEqual({ title: 'caption', subtitle: 'code', status: 'isActive' });
  });

  it('builds columns resolving the caption and the isSystem flag as yes/no text', () => {
    fixture.detectChanges();

    const columns = fixture.componentInstance['columns']();
    const captionColumn = columns.find((column) => column.colId === 'caption');
    expect(captionColumn).toBeTruthy();

    const isSystemColumn = columns.find((column) => column.colId === 'isSystem')!;
    const value = (isSystemColumn.valueGetter as (params: unknown) => string)({ data: listItem });
    expect(value).toBe('No');
  });
});
