import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { describe, expect, it } from 'vitest';
import { NxPaginatorIntl } from './nx-paginator-intl';

describe('NxPaginatorIntl', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        ...provideTransloco({ config: { availableLangs: ['de', 'es'], defaultLang: 'de' } }),
        { provide: MatPaginatorIntl, useClass: NxPaginatorIntl },
      ],
    });
    const transloco = TestBed.inject(TranslocoService);
    transloco.setTranslation(
      {
        shared: {
          paginator: {
            itemsPerPage: 'Einträge pro Seite',
            nextPage: 'Nächste Seite',
            previousPage: 'Vorherige Seite',
            firstPage: 'Erste Seite',
            lastPage: 'Letzte Seite',
            rangeLabel: '{{start}}–{{end}} von {{length}}',
            rangeLabelEmpty: '0 von 0',
          },
        },
      },
      'de',
    );
    transloco.setTranslation(
      {
        shared: {
          paginator: {
            itemsPerPage: 'Elementos por página',
            nextPage: 'Página siguiente',
            previousPage: 'Página anterior',
            firstPage: 'Primera página',
            lastPage: 'Última página',
            rangeLabel: '{{start}}–{{end}} de {{length}}',
            rangeLabelEmpty: '0 de 0',
          },
        },
      },
      'es',
    );
    transloco.setActiveLang('de');
    return { intl: TestBed.inject(MatPaginatorIntl), transloco };
  }

  it('reads labels and range text in de', () => {
    const { intl } = setup();
    expect(intl.itemsPerPageLabel).toBe('Einträge pro Seite');
    expect(intl.nextPageLabel).toBe('Nächste Seite');
    expect(intl.getRangeLabel(0, 25, 4)).toBe('1–4 von 4');
    expect(intl.getRangeLabel(0, 25, 0)).toBe('0 von 0');
  });

  it('switches to es and emits changes', () => {
    const { intl, transloco } = setup();
    let emissions = 0;
    intl.changes.subscribe(() => emissions++);
    transloco.setActiveLang('es');
    expect(intl.itemsPerPageLabel).toBe('Elementos por página');
    expect(intl.lastPageLabel).toBe('Última página');
    expect(intl.getRangeLabel(0, 25, 4)).toBe('1–4 de 4');
    expect(intl.getRangeLabel(1, 10, 15)).toBe('11–15 de 15');
    expect(intl.getRangeLabel(0, 25, 0)).toBe('0 de 0');
    expect(emissions).toBeGreaterThan(0);
  });
});
