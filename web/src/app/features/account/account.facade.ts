import { HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoService } from '@jsverse/transloco';
import { NX_SKIP_ERROR_NAVIGATION } from '../../core/http/skip-error-navigation';
import { Api } from '../../api/platform/api';
import { getBusinessUnit } from '../../api/platform/fn/business-units/get-business-unit';
import { getMe } from '../../api/platform/fn/me/get-me';
import type { BusinessUnit } from '../../api/platform/models/business-unit';
import type { Me } from '../../api/platform/models/me';
import { problemOf } from '../../core/http/problem';
import { NxI18nService } from '../../core/i18n/nx-i18n.service';
import { LANGUAGE_OPTIONS, isSupportedLanguage, type SupportedLanguage } from '../../core/i18n/supported-languages';
import { PermissionsStore } from '../../shared/auth/permissions.store';
import { resolveCaption } from '../../shared/ui/caption/caption.util';

/** One permission as the page shows it, e.g. «Roles — Read». */
export interface AccountPermission {
  readonly code: string;
  readonly label: string;
}

/** The permissions of one resource («Roles»), in the order the API sent them. */
export interface AccountPermissionGroup {
  readonly resource: string;
  readonly resourceLabel: string;
  readonly permissions: readonly AccountPermission[];
}

/**
 * Facade for the «My account» page. `FE-R1`: the page never calls `HttpClient`/the generated API
 * functions directly, only this facade.
 *
 * Also the integration point ficha 0.5 leaves for F5: once `GET /v1/me` answers, it feeds
 * `me.preferredLanguage` into `NxI18nService.setInitialLanguage()` and `me.permissions` into
 * `PermissionsStore.setPermissions()`.
 *
 * Shows names, never ids: the business unit is read by name, the language by its own name, and
 * the permissions (`MePermission` carries only the code, no caption) as «Resource — Action»
 * from `account.permissions.*` (a resource without a translation falls back to its humanized code),
 * grouped by resource.
 */
@Injectable({ providedIn: 'root' })
export class AccountFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);
  private readonly permissionsStore = inject(PermissionsStore);

  /** Emits once the active language's file is loaded (and on every switch): derived texts follow it. */
  private readonly translation = toSignal(this.transloco.selectTranslation());

  private readonly meSignal = signal<Me | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly businessUnitNameSignal = signal<Pick<BusinessUnit, 'code' | 'captionI18n'> | null>(null);

  readonly me = this.meSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  /** Languages to pick from, each in its own name (same list as the top-bar selector). */
  readonly languageOptions = LANGUAGE_OPTIONS;

  /** The language in use now (what the top-bar selector shows). */
  readonly language = computed<SupportedLanguage>(() => {
    const active = this.i18n.activeLang();
    return isSupportedLanguage(active) ? active : 'de';
  });

  /** Business unit by its caption in the active language; `null` without one (or if it could not be read). */
  readonly businessUnitLabel = computed(() => {
    const unit = this.businessUnitNameSignal();
    return unit ? resolveCaption(unit.captionI18n, this.i18n.activeLang(), unit.code) : null;
  });

  /** Permissions grouped by resource, labelled in the active language. */
  readonly permissionGroups = computed<AccountPermissionGroup[]>(() => {
    this.translation();
    const lang = this.i18n.activeLang();
    const groups = new Map<string, AccountPermission[]>();
    for (const permission of this.meSignal()?.permissions ?? []) {
      const [resource, action] = permission.code.split(':');
      const items = groups.get(resource) ?? [];
      items.push({
        code: permission.code,
        label: `${this.resourceLabel(resource, lang)} — ${this.actionLabel(action ?? '', lang)}`,
      });
      groups.set(resource, items);
    }
    // Alphabetical by label in the active language, like the role permission matrix (not the server's order).
    return [...groups]
      .map(([resource, permissions]) => ({
        resource,
        resourceLabel: this.resourceLabel(resource, lang),
        permissions: [...permissions].sort((a, b) => a.label.localeCompare(b.label, lang)),
      }))
      .sort((a, b) => a.resourceLabel.localeCompare(b.resourceLabel, lang));
  });

  /** Monotonic sequence: only the latest `load()` may apply its result. */
  private loadSeq = 0;

  async load(): Promise<void> {
    const seq = ++this.loadSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    try {
      const me = await this.api.invoke(getMe);
      if (seq !== this.loadSeq) {
        return;
      }
      this.meSignal.set(me);
      this.i18n.setInitialLanguage(me.preferredLanguage);
      this.permissionsStore.setPermissions(me.permissions);
      await this.resolveBusinessUnit(me.businessUnitId, seq);
    } catch (error: unknown) {
      if (seq === this.loadSeq) {
        this.errorSignal.set(this.i18n.translateErrorCode(problemOf(error)?.code));
      }
    } finally {
      if (seq === this.loadSeq) {
        this.loadingSignal.set(false);
      }
    }
  }

  /** Same action as the top-bar selector: switches the UI now and saves it (`PATCH /v1/me`). */
  setLanguage(language: SupportedLanguage): void {
    if (language !== this.language()) {
      this.i18n.setLanguage(language);
    }
  }

  private async resolveBusinessUnit(id: string | null, seq: number): Promise<void> {
    if (!id) {
      this.businessUnitNameSignal.set(null);
      return;
    }
    try {
      const unit = await this.api.invoke(getBusinessUnit, { id }, new HttpContext().set(NX_SKIP_ERROR_NAVIGATION, true));
      if (seq === this.loadSeq) {
        this.businessUnitNameSignal.set(unit);
      }
    } catch {
      if (seq === this.loadSeq) {
        this.businessUnitNameSignal.set(null);
      }
    }
  }

  private resourceLabel(resource: string, lang: string): string {
    return this.translated(`account.permissions.resources.${resource}`, lang) ?? humanize(resource);
  }

  private actionLabel(action: string, lang: string): string {
    return this.translated(`account.permissions.actions.${action}`, lang) ?? humanize(action);
  }

  private translated(key: string, lang: string): string | null {
    const text = this.transloco.translate(key, {}, lang);
    return text && text !== key ? text : null;
  }
}

/** `ROLE_ASSIGNMENT` → `Role assignment`: the fallback for a resource nobody translated yet. */
function humanize(code: string): string {
  const words = code.toLowerCase().replace(/_/g, ' ').trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : code;
}
