import { HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, from, map } from 'rxjs';
import { NX_SKIP_ERROR_NAVIGATION } from '../../../core/http/skip-error-navigation';
import { Api } from '../../../api/platform/api';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { createUser } from '../../../api/platform/fn/users/create-user';
import { deactivateUser } from '../../../api/platform/fn/users/deactivate-user';
import { getUser } from '../../../api/platform/fn/users/get-user';
import { listUsers, type ListUsers$Params } from '../../../api/platform/fn/users/list-users';
import { updateUser } from '../../../api/platform/fn/users/update-user';
import type { I18NText } from '../../../api/platform/models/i-18-n-text';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import type { CreateUserRequest } from '../../../api/platform/models/create-user-request';
import type { UpdateUserRequest } from '../../../api/platform/models/update-user-request';
import type { User } from '../../../api/platform/models/user';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import {
  isConcurrencyConflict,
  isDuplicateConflict,
  localizedFieldErrors,
  problemOf,
} from '../../../core/http/problem';
import { SYSTEM_USER_ID, SystemUserReadOnlyError } from '../../../shared/auth/system-user';
import { IdempotentCreateId } from '../../../core/ids/uuid-v7';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import { resolveCaption } from '../../../shared/ui/caption/caption.util';
import type { NxSortModel, Page } from '../../../shared/ui/page.model';

/** How a failed write is to be shown (see `UsersFacade.describeError`). */
export interface UserErrorInfo {
  /**
   * `concurrency`: stale `If-Match`, the interceptor's dialog already told the user (show nothing);
   * `duplicate`/`validation`: mark `fields`; `rule`: a business rule (`ORG-R*`); `generic`: anything else.
   */
  readonly kind: 'concurrency' | 'duplicate' | 'validation' | 'rule' | 'generic';
  /** Translated text for the alert above the form; `null` when there is nothing to say. */
  readonly message: string | null;
  /** Form control name → translated message, for the fields to mark. */
  readonly fields: Readonly<Record<string, readonly string[]>>;
}

/**
 * Facade for the Users master-data screens (`FE-R1`): the list/detail pages
 * never call `HttpClient` or the generated API functions directly, only this
 * facade, which in turn only calls the `ng-openapi-gen`-generated functions
 * through the `Api` helper.
 *
 * `create()`/`update()`/`deactivate()` do NOT catch: the `HttpErrorResponse` propagates and the
 * page asks `describeError()` how to show it. The SYSTEM user is read-only for real (ORG-R6):
 * `update()`/`deactivate()` refuse it with `SystemUserReadOnlyError` before any request.
 */
@Injectable({ providedIn: 'root' })
export class UsersFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);
  private readonly transloco = inject(TranslocoService);

  private readonly selectedSignal = signal<User | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly savingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly businessUnitRefSignal = signal<BusinessUnitListItem | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  /** `true` while `load()` runs. With `error()` set and `selected()` null the page shows «Retry». */
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  /** Translated message of the last failed `load()`; write errors are thrown, not stored here. */
  readonly error = this.errorSignal.asReadonly();
  /** Current business unit of the loaded user (resolved by name, never the id). */
  readonly businessUnitRef = this.businessUnitRefSignal.asReadonly();

  /** User name (also the breadcrumb label); `null` until loaded. */
  readonly title = computed(() => this.selectedSignal()?.fullName ?? null);
  /** ORG-R6: the loaded user is SYSTEM, so the page offers no write action. */
  readonly isSystem = computed(() => this.selectedSignal()?.id === SYSTEM_USER_ID);
  /** Reactive to the active language (reads `activeLang`). */
  readonly resolvedBusinessUnitLabel = computed(() => {
    const ref = this.businessUnitRefSignal();
    return ref ? this.businessUnitLabel(ref) : null;
  });

  private selectedETag: string | null = null;
  private readonly createId = new IdempotentCreateId();
  /** Monotonic sequence: only the latest `load()` (and none issued before a `reset()`) may apply its result. */
  private loadSeq = 0;

  /** `fetch` for `nx-data-grid`. `page` is 0-based (grid convention); the API itself is 1-based. */
  list(page: number, pageSize: number, sort: NxSortModel | null): Observable<Page<UserListItem>> {
    const params: ListUsers$Params = {
      page: page + 1,
      pageSize,
      sort: sort ? `${sort.direction === 'desc' ? '-' : ''}${sort.colId}` : undefined,
    };
    return from(this.api.invoke(listUsers, params)).pipe(
      map((response) => ({
        items: response.items ?? [],
        page,
        pageSize,
        total: response.total,
      })),
    );
  }

  /** Business units for the list's name column (one page; reference data stays small). */
  businessUnits(): Observable<BusinessUnitListItem[]> {
    return this.searchBusinessUnits('');
  }

  /** A language by its own name in the active UI language (`es` → «Spanisch» in German), never the code. */
  languageName(code: string | null | undefined): string {
    if (!code) {
      return '';
    }
    try {
      const lang = this.i18n.getActiveLanguage();
      const name = new Intl.DisplayNames([lang], { type: 'language' }).of(code) ?? code;
      // Spanish/French/Italian names come lowercase («alemán»): a table cell starts with a capital.
      return name.charAt(0).toLocaleUpperCase(lang) + name.slice(1);
    } catch {
      return code;
    }
  }

  /** Clears `selected`/errors, e.g. when navigating from an existing user's detail to "new". */
  reset(): void {
    this.loadSeq++;
    this.loadingSignal.set(false);
    this.selectedSignal.set(null);
    this.selectedETag = null;
    this.errorSignal.set(null);
    this.businessUnitRefSignal.set(null);
  }

  async load(id: string): Promise<void> {
    const seq = ++this.loadSeq;
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.businessUnitRefSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getUser, { id });
      if (seq !== this.loadSeq) {
        return;
      }
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      await this.resolveBusinessUnit(response.body.businessUnitId, seq);
    } catch (error: unknown) {
      if (seq === this.loadSeq) {
        this.selectedSignal.set(null);
        this.errorSignal.set(this.describeError(error).message);
      }
    } finally {
      if (seq === this.loadSeq) {
        this.loadingSignal.set(false);
      }
    }
  }

  async create(request: CreateUserRequest): Promise<User> {
    this.savingSignal.set(true);
    try {
      const response = await this.api.invoke$Response(createUser, {
        body: { ...request, id: this.createId.idFor(request) },
      });
      this.createId.clear();
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async update(id: string, request: UpdateUserRequest): Promise<User> {
    if (id === SYSTEM_USER_ID) {
      throw new SystemUserReadOnlyError();
    }
    this.savingSignal.set(true);
    try {
      const response = await this.api.invoke$Response(updateUser, {
        id,
        'If-Match': this.selectedETag ?? undefined,
        body: request,
      });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      await this.resolveBusinessUnit(response.body.businessUnitId, this.loadSeq);
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /** Propagates errors (the page asks for confirmation with `NxConfirm` first). */
  async deactivate(id: string): Promise<User> {
    if (id === SYSTEM_USER_ID) {
      throw new SystemUserReadOnlyError();
    }
    this.savingSignal.set(true);
    try {
      const response = await this.api.invoke$Response(deactivateUser, {
        id,
        'If-Match': this.selectedETag ?? undefined,
      });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      return response.body;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /**
   * Remote-search function for `nx-entity-picker`. `GET /v1/business-units` has no text filter
   * in the step 0.4 contract (only `page`/`pageSize`/`sort`), so this fetches one page of up to
   * 100 business units and filters client-side on `code` and the caption in the active language.
   */
  searchBusinessUnits = (query: string): Observable<BusinessUnitListItem[]> => {
    return from(this.api.invoke(listBusinessUnits, { page: 1, pageSize: 100, sort: 'code' })).pipe(
      map((response) => {
        const items = response.items ?? [];
        const needle = query.trim().toLowerCase();
        if (!needle) {
          return items;
        }
        return items.filter(
          (bu) => bu.code.toLowerCase().includes(needle) || this.caption(bu).toLowerCase().includes(needle),
        );
      }),
    );
  };

  /** «code — caption» of a business unit (the caption follows the active language). */
  formatBusinessUnit(bu: { code: string; captionI18n: I18NText }): string {
    return this.businessUnitLabel(bu);
  }

  /**
   * How to show a failed `create()`/`update()`/`deactivate()`/`load()`:
   * - 409 `CONCURRENCY`: `kind: 'concurrency'`, no message (the interceptor's dialog already ran);
   * - 409 `CONFLICT` (the `oid` is already linked to another user): marks `oid`;
   * - 422 `ORG-R9` (duplicate email): marks `email`; other 422 codes: translated rule text;
   * - 400: `errors.validation` + the named fields marked (server texts are English: generic text per field);
   * - anything else: the generic message.
   */
  describeError(error: unknown): UserErrorInfo {
    if (isConcurrencyConflict(error)) {
      return { kind: 'concurrency', message: null, fields: {} };
    }
    if (error instanceof SystemUserReadOnlyError) {
      return { kind: 'rule', message: this.i18n.translateErrorCode('ORG-R6'), fields: {} };
    }
    const status = (error as { status?: number } | null)?.status;
    const problem = problemOf(error);
    if (isDuplicateConflict(error)) {
      return {
        kind: 'duplicate',
        message: null,
        fields: { oid: [this.transloco.translate('org.users.errors.oidTaken')] },
      };
    }
    if (status === 400) {
      return {
        kind: 'validation',
        message: this.transloco.translate('errors.validation'),
        fields: this.toControlNames(
          localizedFieldErrors(problem?.errors, this.transloco.translate('shared.form.invalidValue')),
        ),
      };
    }
    if (problem?.code === 'ORG-R9') {
      return {
        kind: 'rule',
        message: null,
        fields: { email: [this.i18n.translateErrorCode('ORG-R9')] },
      };
    }
    if (status === 422 && problem?.code) {
      return { kind: 'rule', message: this.i18n.translateErrorCode(problem.code), fields: {} };
    }
    return { kind: 'generic', message: this.i18n.translateErrorCode(problem?.code), fields: {} };
  }

  /** The API names the business unit `businessUnitId`; the form control is `businessUnit`. */
  private toControlNames(fields: Readonly<Record<string, readonly string[]>>): Record<string, readonly string[]> {
    const result: Record<string, readonly string[]> = {};
    for (const [field, messages] of Object.entries(fields)) {
      result[field === 'businessUnitId' ? 'businessUnit' : field] = messages;
    }
    return result;
  }

  private businessUnitLabel(bu: { code: string; captionI18n: I18NText }): string {
    return `${bu.code} — ${this.caption(bu)}`;
  }

  private caption(bu: { code: string; captionI18n: I18NText }): string {
    return resolveCaption(bu.captionI18n, this.i18n.getActiveLanguage(), bu.code);
  }

  /** Resolves the user's business unit by name; a failed lookup leaves it unresolved (the page shows «—»). */
  private async resolveBusinessUnit(id: string | null, seq: number): Promise<void> {
    if (!id) {
      this.businessUnitRefSignal.set(null);
      return;
    }
    try {
      const bu = await this.api.invoke(getBusinessUnit, { id }, new HttpContext().set(NX_SKIP_ERROR_NAVIGATION, true));
      if (seq === this.loadSeq) {
        this.businessUnitRefSignal.set(bu);
      }
    } catch {
      if (seq === this.loadSeq) {
        this.businessUnitRefSignal.set(null);
      }
    }
  }
}
