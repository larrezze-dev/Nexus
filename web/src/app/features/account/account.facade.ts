import { Injectable, inject, signal } from '@angular/core';
import { Api } from '../../api/platform/api';
import { getMe } from '../../api/platform/fn/me/get-me';
import { Me } from '../../api/platform/models/me';
import { isProblem } from '../../core/http/problem';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';
import { PermissionsStore } from '../../shared/auth/permissions.store';

/**
 * Facade for the "My account" page. `FE-R1` (`conventions-frontend.md §2`): the page component
 * never calls `HttpClient`/the generated API functions directly, only this facade — which in
 * turn only calls the `ng-openapi-gen`-generated `getMe` function (through the `Api` helper
 * ng-openapi-gen generates in "functions" style), never `HttpClient` itself.
 *
 * Also the integration point ficha 0.5 leaves for F5: once `GET /v1/me` answers, it feeds
 * `me.preferredLanguage` into `NxI18nService.setInitialLanguage()` and `me.permissions` into
 * `PermissionsStore.setPermissions()` — the two hooks `core/i18n` and `shared/auth` documented
 * for this subtask.
 */
@Injectable({ providedIn: 'root' })
export class AccountFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);
  private readonly permissionsStore = inject(PermissionsStore);

  private readonly meSignal = signal<Me | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly me = this.meSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async load(): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    try {
      const me = await this.api.invoke(getMe);
      this.meSignal.set(me);
      this.i18n.setInitialLanguage(me.preferredLanguage);
      this.permissionsStore.setPermissions(me.permissions);
    } catch (error: unknown) {
      this.errorSignal.set(this.describe(error));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  private describe(error: unknown): string {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body) && body.detail) {
        return body.detail;
      }
    }
    return this.i18n.translateErrorCode(undefined);
  }
}
