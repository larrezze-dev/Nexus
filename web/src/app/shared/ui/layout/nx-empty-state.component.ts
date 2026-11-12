import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

/** The designed view states of `conventions-frontend.md §4.7` that show a message instead of data. */
export type NxEmptyStateKind = 'empty' | 'no-results' | 'error' | 'forbidden' | 'not-found';

const ICONS: Readonly<Record<NxEmptyStateKind, string>> = {
  empty: 'inbox',
  'no-results': 'filter_alt_off',
  error: 'cloud_off',
  forbidden: 'lock',
  'not-found': 'search_off',
};

/** i18n group under `shared.emptyState.*` with the default title/message of each kind. */
const KEY_GROUP: Readonly<Record<NxEmptyStateKind, string>> = {
  empty: 'empty',
  'no-results': 'noResults',
  error: 'error',
  forbidden: 'forbidden',
  'not-found': 'notFound',
};

/**
 * Empty / no results / error / 403 / 404 state (`conventions-frontend.md §4.7`, canvas board 5):
 * an icon in a tinted circle, a title (18/24 500), a muted sentence and an optional action
 * (projected content, e.g. «New warehouse», «Clear filters», «Retry»). Title and message are
 * already-translated strings; when omitted, a generic text for the kind is shown. The `error`
 * kind is announced to screen readers (`role="alert"`).
 */
@Component({
  selector: 'nx-empty-state',
  standalone: true,
  imports: [MatIconModule, TranslocoPipe],
  template: `
    <div class="nx-empty-state__icon" aria-hidden="true">
      <mat-icon>{{ resolvedIcon() }}</mat-icon>
    </div>
    @let heading = title() || ('shared.emptyState.' + keyGroup() + '.title' | transloco);
    @if (headingLevel() === 1) {
      <h1 class="nx-empty-state__title">{{ heading }}</h1>
    } @else {
      <h2 class="nx-empty-state__title">{{ heading }}</h2>
    }
    @if (message() !== '') {
      <p class="nx-empty-state__message">
        {{ message() || ('shared.emptyState.' + keyGroup() + '.message' | transloco) }}
      </p>
    }
    <div class="nx-empty-state__actions">
      <ng-content />
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--nx-space-3);
      padding: var(--nx-space-6) var(--nx-space-5);
      text-align: center;
      color: var(--nx-color-text);
    }

    .nx-empty-state__icon {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 56px;
      height: 56px;
      border-radius: 50%;
      background: var(--nx-color-icon-container);
      color: var(--nx-color-primary);
    }

    .nx-empty-state__icon mat-icon {
      width: 28px;
      height: 28px;
      font-size: 28px;
    }

    :host(.nx-empty-state--error) .nx-empty-state__icon {
      background: var(--nx-alert-error-bg);
      color: var(--nx-color-error);
    }

    :host(.nx-empty-state--forbidden) .nx-empty-state__icon {
      background: var(--nx-alert-warning-bg);
      color: var(--nx-status-warning-fg);
    }

    .nx-empty-state__title {
      margin: 0;
      font: var(--nx-text-section-title);
    }

    .nx-empty-state__message {
      margin: 0;
      max-width: 420px;
      font: var(--nx-text-body);
      color: var(--nx-color-text-muted);
    }

    .nx-empty-state__actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--nx-space-2);
      margin-top: var(--nx-space-1);
    }

    .nx-empty-state__actions:empty {
      display: none;
    }
  `,
  host: {
    '[class]': "'nx-empty-state-host nx-empty-state--' + kind()",
    '[attr.role]': "kind() === 'error' ? 'alert' : null",
    '[attr.data-kind]': 'kind()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxEmptyStateComponent {
  readonly kind = input<NxEmptyStateKind>('empty');

  /** Title, already translated; empty/omitted → generic title of the kind. */
  readonly title = input<string | null | undefined>(undefined);

  /** Sentence under the title, already translated; omitted → generic text; `''` → no sentence. */
  readonly message = input<string | null | undefined>(undefined);

  /** Material icon name overriding the kind's icon (e.g. `warehouse` for an empty warehouse list). */
  readonly icon = input<string | null | undefined>(undefined);

  /** Heading level of the title: `1` when the state is the whole page (403/404), `2` inside a page. */
  readonly headingLevel = input<1 | 2>(2);

  protected readonly resolvedIcon = computed(() => this.icon() || ICONS[this.kind()]);
  protected readonly keyGroup = computed(() => KEY_GROUP[this.kind()]);
}
