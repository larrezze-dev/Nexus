import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
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
  let navigateSpy: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    navigateSpy = vi.fn().mockResolvedValue(true);

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
        {
          provide: RolesFacade,
          useValue: { list: () => of<Page<RoleListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }) },
        },
        { provide: Router, useValue: { navigate: navigateSpy } },
        { provide: ActivatedRoute, useValue: {} },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RoleListPage);
  });

  it('navigates to the detail route when exactly one row is selected', () => {
    fixture.detectChanges();

    fixture.componentInstance['onSelectionChanged']([listItem]);

    expect(navigateSpy).toHaveBeenCalledWith(['..', listItem.id], { relativeTo: expect.anything() });
  });

  it('does not navigate when zero or more than one row is selected', () => {
    fixture.detectChanges();

    fixture.componentInstance['onSelectionChanged']([]);
    fixture.componentInstance['onSelectionChanged']([listItem, { ...listItem, id: 'other' }]);

    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('builds columns resolving the caption and the isSystem flag as yes/no text', () => {
    fixture.detectChanges();

    const columns = fixture.componentInstance['columns']();
    const captionColumn = columns.find((column) => !column.field && column.valueGetter);
    expect(captionColumn).toBeTruthy();

    const isSystemColumn = columns[2];
    const value = (isSystemColumn.valueGetter as (params: unknown) => string)({ data: listItem });
    expect(value).toBe('No');
  });
});
