import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { NxI18nService } from './nx-i18n.service';
import { LANGUAGE_OPTIONS, type SupportedLanguage } from './supported-languages';

/**
 * Language switcher, meant to be hosted inside the user menu (conventions-frontend.md §1/§7).
 * Standalone and self-contained: `core/layout` (another track) inserts it where it builds
 * the user menu — this component does not assume anything about its host's layout.
 *
 * No `mat-icon` glyph is used here: `index.html` does not link the Material Icons font in
 * this worktree, and a missing font would render a literal icon-name fallback instead.
 */
@Component({
  selector: 'nx-language-selector',
  imports: [MatButtonModule, MatMenuModule],
  template: `
    <button mat-menu-item [matMenuTriggerFor]="languageMenu" type="button">
      <span>{{ currentLabel() }}</span>
    </button>
    <mat-menu #languageMenu="matMenu">
      @for (language of languages; track language.code) {
        <button mat-menu-item type="button" (click)="select(language.code)">
          {{ language.label }}
        </button>
      }
    </mat-menu>
  `,
})
export class LanguageSelectorComponent {
  private readonly i18n = inject(NxI18nService);

  readonly languages = LANGUAGE_OPTIONS;

  readonly currentLabel = computed(() => {
    const active = this.i18n.activeLang();
    return this.languages.find((language) => language.code === active)?.label ?? active;
  });

  select(code: SupportedLanguage): void {
    this.i18n.setLanguage(code);
  }
}
