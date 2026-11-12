import { Injectable, Injector, Provider, inject, runInInjectionContext } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { of, switchMap } from 'rxjs';
import { NxBreadcrumbService } from './breadcrumb.service';

const APP_NAME = 'NEXUS';

/**
 * Sets `document.title` to "<translated page title> · NEXUS" from the route data (see
 * `NxBreadcrumbService` for the data contract). Follows language changes, and for detail pages
 * switches from the list name to the record name once the page published it.
 */
@Injectable({ providedIn: 'root' })
export class NxTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly injector = inject(Injector);
  private started = false;

  /**
   * The Router builds its `TitleStrategy` while it is itself being constructed, and the
   * breadcrumb service needs the Router, so the reactive wiring starts on the first
   * `updateTitle` call (the Router exists by then) instead of in the constructor.
   */
  override updateTitle(_snapshot: RouterStateSnapshot): void {
    if (this.started) {
      return;
    }
    this.started = true;
    runInInjectionContext(this.injector, () => {
      const breadcrumb = inject(NxBreadcrumbService);
      toObservable(breadcrumb.pageTitle)
        .pipe(
          switchMap((source) => {
            if (!source) {
              return of(null);
            }
            if (source.text) {
              return of(source.text);
            }
            return source.key ? this.transloco.selectTranslate(source.key) : of(null);
          }),
          takeUntilDestroyed(),
        )
        .subscribe((text) => this.title.setTitle(text ? `${text} · ${APP_NAME}` : APP_NAME));
    });
  }
}

/** Add to `app.config.ts` providers. */
export function provideNexusTitleStrategy(): Provider {
  return { provide: TitleStrategy, useExisting: NxTitleStrategy };
}
