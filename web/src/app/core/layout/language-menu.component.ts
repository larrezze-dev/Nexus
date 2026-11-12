import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe } from '@jsverse/transloco';
import { NxI18nService } from '../i18n/nx-i18n.service';
import { LANGUAGE_OPTIONS, type SupportedLanguage } from '../i18n/supported-languages';

/**
 * Top-bar language selector (`conventions-frontend.md §4.2`): a button showing the active
 * language in its own name, opening a menu with the five languages, each in its own name.
 * Picking one switches the whole UI at once through `NxI18nService.setLanguage()`, which also
 * remembers it locally and hands it to `NX_LANGUAGE_PREFERENCE_SAVER` (`PATCH /v1/me`).
 */
@Component({
  selector: 'nx-shell-language-menu',
  imports: [MatButtonModule, MatIconModule, MatMenuModule, TranslocoPipe],
  template: `
    <button
      mat-button
      type="button"
      class="nx-language-menu__trigger"
      [matMenuTriggerFor]="languageMenu"
      [attr.aria-label]="'shell.language.ariaLabel' | transloco: { language: currentLabel() }"
      data-testid="nx-shell-language-trigger"
    >
      <mat-icon class="nx-language-menu__icon" aria-hidden="true">language</mat-icon>
      <span>{{ currentLabel() }}</span>
      <mat-icon class="nx-language-menu__caret" iconPositionEnd aria-hidden="true">arrow_drop_down</mat-icon>
    </button>
    <mat-menu #languageMenu="matMenu" xPosition="before">
      @for (language of languages; track language.code) {
        <button
          mat-menu-item
          type="button"
          role="menuitemradio"
          [attr.aria-checked]="language.code === active()"
          [attr.lang]="language.code"
          [attr.data-testid]="'nx-shell-language-' + language.code"
          (click)="select(language.code)"
        >
          <mat-icon aria-hidden="true">{{ language.code === active() ? 'check' : '' }}</mat-icon>
          <span>{{ language.label }}</span>
        </button>
      }
    </mat-menu>
  `,
  styles: `
    .nx-language-menu__trigger {
      --mat-button-text-label-text-color: var(--nx-color-text);
      --mat-button-text-horizontal-padding: var(--nx-space-3);
      --mat-button-text-icon-spacing: var(--nx-space-2);
      --mat-button-text-icon-offset: calc(-1 * var(--nx-space-1));
    }

    .nx-language-menu__icon {
      color: var(--nx-color-text-muted);
    }

    /* Compact: the label stays visible, the decorative globe goes. */
    @media (max-width: 599.98px) {
      .nx-language-menu__trigger .nx-language-menu__icon {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LanguageMenuComponent {
  private readonly i18n = inject(NxI18nService);

  protected readonly languages = LANGUAGE_OPTIONS;
  protected readonly active = this.i18n.activeLang;

  protected readonly currentLabel = computed(() => {
    const active = this.active();
    return this.languages.find((language) => language.code === active)?.label ?? active;
  });

  protected select(code: SupportedLanguage): void {
    if (code !== this.active()) {
      this.i18n.setLanguage(code);
    }
  }
}
