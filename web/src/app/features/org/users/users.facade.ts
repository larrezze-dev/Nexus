import { Injectable, inject, signal } from '@angular/core';
import { Observable, from, map } from 'rxjs';
import { Api } from '../../../api/platform/api';
import { getBusinessUnit } from '../../../api/platform/fn/business-units/get-business-unit';
import { listBusinessUnits } from '../../../api/platform/fn/business-units/list-business-units';
import { createUser } from '../../../api/platform/fn/users/create-user';
import { deactivateUser } from '../../../api/platform/fn/users/deactivate-user';
import { getUser } from '../../../api/platform/fn/users/get-user';
import { listUsers, type ListUsers$Params } from '../../../api/platform/fn/users/list-users';
import { updateUser } from '../../../api/platform/fn/users/update-user';
import type { BusinessUnitListItem } from '../../../api/platform/models/business-unit-list-item';
import type { CreateUserRequest } from '../../../api/platform/models/create-user-request';
import type { UpdateUserRequest } from '../../../api/platform/models/update-user-request';
import type { User } from '../../../api/platform/models/user';
import type { UserListItem } from '../../../api/platform/models/user-list-item';
import { isProblem, type Problem } from '../../../core/http/problem';
import { NxI18nService } from '../../../core/i18n/nx-i18n.service';
import type { NxSortModel, Page } from '../../../shared/ui/page.model';

/**
 * `app_user.id` of the SYSTEM user seeded by the platform (ORG-R6: never
 * edited, never deactivated, no `external_subject`). Fixed across every
 * environment; the UI uses it only to hide actions it would reject anyway.
 */
export const SYSTEM_USER_ID = '00000000-0000-7000-8000-000000000001';

/** Form-level errors this facade knows how to attribute to a specific field. */
export interface UserFieldErrors {
  readonly email?: string;
}

/**
 * Facade for the Users master-data screens (`FE-R1`): the list/detail pages
 * never call `HttpClient` or the generated API functions directly, only this
 * facade, which in turn only calls the `ng-openapi-gen`-generated functions
 * through the `Api` helper.
 */
@Injectable({ providedIn: 'root' })
export class UsersFacade {
  private readonly api = inject(Api);
  private readonly i18n = inject(NxI18nService);

  private readonly selectedSignal = signal<User | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly savingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly fieldErrorsSignal = signal<UserFieldErrors | null>(null);

  readonly selected = this.selectedSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly saving = this.savingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly fieldErrors = this.fieldErrorsSignal.asReadonly();

  private selectedETag: string | null = null;

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

  /** Clears `selected`/errors, e.g. when navigating from an existing user's detail to "new". */
  reset(): void {
    this.selectedSignal.set(null);
    this.selectedETag = null;
    this.errorSignal.set(null);
    this.fieldErrorsSignal.set(null);
  }

  async load(id: string): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.fieldErrorsSignal.set(null);
    try {
      const response = await this.api.invoke$Response(getUser, { id });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
    } catch (error: unknown) {
      this.applyError(error);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async create(request: CreateUserRequest): Promise<User | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    this.fieldErrorsSignal.set(null);
    try {
      const response = await this.api.invoke$Response(createUser, { body: request });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.applyError(error);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  async update(id: string, request: UpdateUserRequest): Promise<User | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    this.fieldErrorsSignal.set(null);
    try {
      const response = await this.api.invoke$Response(updateUser, {
        id,
        'If-Match': this.selectedETag ?? undefined,
        body: request,
      });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.applyError(error);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /** ORG-R6: callers must not offer this action for `SYSTEM_USER_ID` (also rejected server-side). */
  async deactivate(id: string): Promise<User | null> {
    this.savingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const response = await this.api.invoke$Response(deactivateUser, {
        id,
        'If-Match': this.selectedETag ?? undefined,
      });
      this.selectedSignal.set(response.body);
      this.selectedETag = response.headers.get('ETag');
      return response.body;
    } catch (error: unknown) {
      this.applyError(error);
      return null;
    } finally {
      this.savingSignal.set(false);
    }
  }

  /**
   * Remote-search function for `nx-entity-picker`. `GET /v1/business-units` has no text filter
   * in the step 0.4 contract (only `page`/`pageSize`/`sort`), so this fetches one page of up to
   * 100 business units and filters client-side on `code` and the `de` caption.
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
          (bu) => bu.code.toLowerCase().includes(needle) || bu.captionI18n.de.toLowerCase().includes(needle),
        );
      }),
    );
  };

  /** Label for a business unit already assigned to a user, shown read-only in the edit form. */
  async describeBusinessUnit(id: string): Promise<string> {
    const bu = await this.api.invoke(getBusinessUnit, { id });
    return this.formatBusinessUnit(bu);
  }

  formatBusinessUnit(bu: { code: string; captionI18n: { de: string } }): string {
    return `${bu.code} — ${bu.captionI18n.de}`;
  }

  private applyError(error: unknown): void {
    const problem = this.extractProblem(error);
    if (problem?.code === 'ORG-R9') {
      this.fieldErrorsSignal.set({ email: this.i18n.translateErrorCode(problem.code) });
      return;
    }
    this.errorSignal.set(problem?.detail ?? this.i18n.translateErrorCode(problem?.code));
  }

  private extractProblem(error: unknown): Problem | undefined {
    if (error && typeof error === 'object' && 'error' in error) {
      const body = (error as { error?: unknown }).error;
      if (isProblem(body)) {
        return body;
      }
    }
    return undefined;
  }
}
