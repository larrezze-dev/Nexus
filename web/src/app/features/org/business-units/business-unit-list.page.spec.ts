import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import { Page } from '../../../shared/ui/page.model';
import { BusinessUnitsFacade } from './business-units.facade';
import { BusinessUnitListPage } from './business-unit-list.page';

const listItem: BusinessUnitListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c50',
  legalEntityId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  code: 'BU-01',
  captionI18n: { de: 'Vermietung', en: 'Rental' },
  isActive: true,
};

describe('BusinessUnitListPage', () => {
  let fixture: ComponentFixture<BusinessUnitListPage>;
  let navigateSpy: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    navigateSpy = vi.fn().mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [
        BusinessUnitListPage,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              org: { businessUnits: { title: 'Business units', new: 'New', fields: {}, empty: 'No rows' } },
              shared: { dataGrid: { empty: 'No rows' } },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        {
          provide: BusinessUnitsFacade,
          useValue: { list: () => of<Page<BusinessUnitListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }) },
        },
        { provide: Router, useValue: { navigate: navigateSpy } },
        { provide: ActivatedRoute, useValue: {} },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BusinessUnitListPage);
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
});
