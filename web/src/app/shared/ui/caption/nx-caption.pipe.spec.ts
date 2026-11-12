import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService, provideTransloco } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { I18NText } from '../../../api/platform/models/i-18-n-text';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from './caption.util';
import { NxCaptionPipe } from './nx-caption.pipe';

describe('resolveCaption', () => {
  const caption = { de: 'Lager', es: 'Almacén' } as I18NText;

  it('falls back active -> de -> en -> first -> fallback', () => {
    expect(resolveCaption(caption, 'es', 'X')).toBe('Almacén');
    expect(resolveCaption(caption, 'fr', 'X')).toBe('Lager');
    expect(resolveCaption({ en: 'Store' } as I18NText, 'fr', 'X')).toBe('Store');
    expect(resolveCaption({ it: 'Magazzino' } as I18NText, 'fr', 'X')).toBe('Magazzino');
    expect(resolveCaption({} as I18NText, 'fr', 'X')).toBe('X');
    expect(resolveCaption(null, 'fr', 'X')).toBe('X');
  });
});

@Component({
  imports: [NxCaptionPipe],
  template: `<span data-testid="out">{{ caption | nxCaption: 'CODE' }}</span>`,
})
class HostComponent {
  caption = { de: 'Lager', en: 'Warehouse', es: 'Almacén' } as I18NText;
}

describe('NxCaptionPipe', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [...provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'es'], defaultLang: 'de' } })],
    });
  });

  it('follows the active language, with fallback to de', async () => {
    TestBed.inject(NxI18nService);
    const transloco = TestBed.inject(TranslocoService);
    transloco.setActiveLang('de');
    const fixture = TestBed.createComponent(HostComponent);
    const text = () => (fixture.nativeElement as HTMLElement).querySelector('[data-testid=out]')?.textContent;
    fixture.detectChanges();
    expect(text()).toBe('Lager');

    transloco.setActiveLang('es');
    fixture.detectChanges();
    expect(text()).toBe('Almacén');

    transloco.setActiveLang('fr');
    fixture.detectChanges();
    expect(text()).toBe('Lager');
  });
});
