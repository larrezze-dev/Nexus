import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { LegalEntityListItem } from '../../../api/platform/models/legal-entity-list-item';
import { Page } from '../../../shared/ui/page.model';
import { LegalEntitiesFacade } from './legal-entities.facade';
import { LegalEntityListPage } from './legal-entity-list.page';

const listItem: LegalEntityListItem = {
  id: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c40',
  groupId: '018f1a3c-7e3b-7c5a-9c2e-6a2f8e9b0c30',
  code: 'LE-CH',
  currencyCode: 'CHF',
  countryCode: 'CH',
  isActive: true,
};

describe('LegalEntityListPage', () => {
  let fixture: ComponentFixture<LegalEntityListPage>;
  let navigateSpy: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    navigateSpy = vi.fn().mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [
        LegalEntityListPage,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              org: { legalEntities: { title: 'Legal entities', new: 'New', fields: {}, empty: 'No rows' } },
              shared: { dataGrid: { empty: 'No rows' } },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        {
          provide: LegalEntitiesFacade,
          useValue: { list: () => of<Page<LegalEntityListItem>>({ items: [listItem], page: 0, pageSize: 25, total: 1 }) },
        },
        { provide: Router, useValue: { navigate: navigateSpy } },
        { provide: ActivatedRoute, useValue: {} },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LegalEntityListPage);
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
