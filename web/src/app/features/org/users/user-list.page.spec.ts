import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTransloco } from '@jsverse/transloco';
import { of } from 'rxjs';
import { UserListPage } from './user-list.page';
import { UsersFacade } from './users.facade';

describe('UserListPage', () => {
  it('renders the title and fetches through the facade (FE-R1: never HttpClient directly)', () => {
    const list = vi.fn(() => of({ items: [], page: 0, pageSize: 25, total: 0 }));

    TestBed.configureTestingModule({
      imports: [UserListPage],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        ...provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en' },
        }),
        { provide: UsersFacade, useValue: { list } },
      ],
    });

    const fixture = TestBed.createComponent(UserListPage);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.length).toBeGreaterThan(0);
    // `nx-data-grid` calls `fetch()` once on creation.
    expect(list).toHaveBeenCalled();
  });
});
