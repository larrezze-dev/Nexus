import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { NxSortModel, Page } from '../../../shared/ui/page.model';
import type { PartyListItem } from '../../../api/platform/models/party-list-item';
import { PartyListPage } from './party-list.page';
import { PartiesFacade } from './parties.facade';

describe('PartyListPage', () => {
  it('fetches through the facade only (FE-R1) and re-fetches when a filter changes', () => {
    const list = vi.fn(
      (
        _page: number,
        _pageSize: number,
        _sort: NxSortModel | null,
        _filters: Record<string, unknown>,
      ) => of<Page<PartyListItem>>({ items: [], page: 0, pageSize: 25, total: 0 }),
    );

    TestBed.configureTestingModule({
      imports: [PartyListPage],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        ...provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' } }),
        { provide: PartiesFacade, useValue: { list } },
      ],
    });

    const fixture = TestBed.createComponent(PartyListPage);
    fixture.detectChanges();

    expect(list).toHaveBeenCalled();
    const callsBefore = list.mock.calls.length;

    fixture.componentInstance['filterForm'].controls.q.setValue('acme');
    fixture.detectChanges();

    expect(list.mock.calls.length).toBeGreaterThan(callsBefore);
    const lastFilters = list.mock.calls.at(-1)?.[3];
    expect(lastFilters).toEqual({ q: 'acme', partyType: undefined, isInternal: undefined });
  });
});
