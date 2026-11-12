import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { describe, expect, it } from 'vitest';
import { ForbiddenPage } from './forbidden.page';
import { NotFoundPage } from './not-found.page';

describe.each([
  ['forbidden', ForbiddenPage, 'nx-forbidden-page'],
  ['not-found', NotFoundPage, 'nx-not-found-page'],
] as const)('%s page', (kind, page, testId) => {
  it('renders nx-empty-state of its kind with the account action', () => {
    TestBed.configureTestingModule({
      imports: [page],
      providers: [
        provideRouter([]),
        provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
      ],
    });
    const fixture = TestBed.createComponent(page);
    fixture.detectChanges();
    const state = (fixture.nativeElement as HTMLElement).querySelector(`nx-empty-state[data-testid="${testId}"]`);
    expect(state?.getAttribute('data-kind')).toBe(kind);
    expect(state?.querySelector('a[href="/account"]')).toBeTruthy();
  });
});
