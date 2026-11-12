import { TranslocoTestingModule } from '@jsverse/transloco';

/** English test translations of the `shared.layout.*` / `shared.emptyState.*` keys used by the layout components. */
export const LAYOUT_TEST_EN = {
  common: { cancel: 'Cancel' },
  breadcrumb: { ariaLabel: 'Breadcrumb' },
  shared: {
    layout: {
      pageHeader: { count: '{{count}} records', countOne: '1 record' },
      filterBar: { clear: 'Clear filters', ariaLabel: 'Filters' },
      sectionIndex: { title: 'On this page' },
      confirm: { confirm: 'Confirm' },
    },
    emptyState: {
      empty: { title: 'No records yet', message: 'Once records are created, they appear here.' },
      noResults: { title: 'No records match', message: 'Try other filters.' },
      error: { title: 'Could not load', message: 'Try again.' },
      forbidden: { title: 'No access', message: 'Ask an administrator.' },
      notFound: { title: 'Not found', message: 'The record no longer exists.' },
    },
  },
};

export function layoutTransloco() {
  return TranslocoTestingModule.forRoot({
    langs: { en: LAYOUT_TEST_EN },
    translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
  });
}
