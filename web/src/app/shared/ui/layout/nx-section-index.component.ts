import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  input,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** One entry of the section index: the `id` of an `nx-form-section` and its (translated) label. */
export interface NxSectionIndexItem {
  readonly id: string;
  readonly label: string;
}

/**
 * Section index for long create forms (> 4 sections, `conventions-frontend.md §4.6`). Wraps the
 * form: the projected sections on the left and a sticky «On this page» index on the right
 * (200 px, ≥ 1280 px only; below that the index is hidden and the form takes the full width).
 * Clicking an entry scrolls to the section with that `id`; the entry of the section in view is
 * marked as current.
 *
 * ```html
 * <nx-section-index [sections]="[{ id: 'company', label: … }, …]">
 *   <nx-form-section id="company" …>…</nx-form-section>
 * </nx-section-index>
 * ```
 */
@Component({
  selector: 'nx-section-index',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="nx-section-index__content">
      <ng-content />
    </div>
    <nav
      class="nx-section-index__nav"
      [attr.aria-label]="'shared.layout.sectionIndex.title' | transloco"
    >
      <span class="nx-section-index__title" aria-hidden="true">{{
        'shared.layout.sectionIndex.title' | transloco
      }}</span>
      @for (section of sections(); track section.id) {
        <a
          class="nx-section-index__link"
          [class.nx-section-index__link--current]="current() === section.id"
          [attr.aria-current]="current() === section.id ? 'location' : null"
          [attr.href]="'#' + section.id"
          (click)="go($event, section.id)"
        >
          {{ section.label }}
        </a>
      }
    </nav>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      gap: var(--nx-space-5);
      min-width: 0;
    }

    .nx-section-index__content {
      flex: 1 1 auto;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: var(--nx-space-5);
    }

    .nx-section-index__nav {
      flex: 0 0 200px;
      position: sticky;
      top: var(--nx-space-5);
      display: flex;
      flex-direction: column;
      gap: 0;
    }

    .nx-section-index__title {
      padding: 0 var(--nx-space-3) var(--nx-space-2);
      font: var(--nx-text-label);
      color: var(--nx-color-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.3px;
    }

    .nx-section-index__link {
      display: block;
      padding: var(--nx-space-2) var(--nx-space-3);
      border-left: 2px solid var(--nx-color-border);
      font: var(--nx-text-body);
      color: var(--nx-color-text-muted);
      text-decoration: none;
    }

    .nx-section-index__link:hover {
      color: var(--nx-color-text);
    }

    .nx-section-index__link--current {
      border-left-color: var(--nx-color-primary);
      color: var(--nx-color-primary);
      font-weight: 500;
    }

    @media (max-width: 1279.98px) {
      .nx-section-index__nav {
        display: none;
      }
    }
  `,
  host: { class: 'nx-section-index' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxSectionIndexComponent {
  readonly sections = input.required<readonly NxSectionIndexItem[]>();

  /** `id` of the section marked as current (first one until the user scrolls or clicks). */
  protected readonly current = signal<string | null>(null);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.current.set(this.sections()[0]?.id ?? null);
      if (typeof IntersectionObserver === 'undefined') {
        return;
      }
      // A section counts as "in view" once its top passes the upper 30 % of the viewport.
      const observer = new IntersectionObserver(
        (entries) => {
          const visible = entries.filter((entry) => entry.isIntersecting);
          if (visible.length > 0) {
            this.current.set((visible[0].target as HTMLElement).id);
          }
        },
        { rootMargin: '0px 0px -70% 0px' },
      );
      for (const section of this.sections()) {
        const element = this.host.nativeElement.querySelector(`#${CSS.escape(section.id)}`);
        if (element) {
          observer.observe(element);
        }
      }
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  protected go(event: Event, id: string): void {
    event.preventDefault();
    this.current.set(id);
    const target = this.host.nativeElement.querySelector<HTMLElement>(`#${CSS.escape(id)}`);
    target?.scrollIntoView?.({ behavior: 'smooth', block: 'start' });
    // Move focus to the section heading so keyboard and screen-reader users land there too.
    const heading = target?.querySelector<HTMLElement>('h2');
    if (heading) {
      heading.setAttribute('tabindex', '-1');
      heading.focus({ preventScroll: true });
    }
  }
}
