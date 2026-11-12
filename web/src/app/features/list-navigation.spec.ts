import { Component, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { RoleListPage } from './access/roles/role-list.page';
import { RolesFacade } from './access/roles/roles.facade';
import { BusinessUnitListPage } from './org/business-units/business-unit-list.page';
import { BusinessUnitsFacade } from './org/business-units/business-units.facade';
import { LegalEntitiesFacade } from './org/legal-entities/legal-entities.facade';
import { LegalEntityListPage } from './org/legal-entities/legal-entity-list.page';
import { PartiesFacade } from './org/parties/parties.facade';
import { PartyListPage } from './org/parties/party-list.page';
import { UserListPage } from './org/users/user-list.page';
import { UsersFacade } from './org/users/users.facade';

@Component({ selector: 'nx-test-detail', template: '' })
class DetailStubComponent {}

const empty = { list: () => of({ items: [], page: 0, pageSize: 25, total: 0 }) };

/**
 * A list page opens its record at `/<section>/<id>` with the REAL router and the same
 * route shape as the app (`''` child for the list, `:id` sibling). A mocked `Router` cannot
 * catch a wrong relative path (`['..', id]` from an empty-path child landed on `/<id>`).
 */
describe.each<[string, string, Type<unknown>, Type<unknown>]>([
  ['legal entities', 'legal-entities', LegalEntityListPage, LegalEntitiesFacade],
  ['business units', 'business-units', BusinessUnitListPage, BusinessUnitsFacade],
  ['roles', 'roles', RoleListPage, RolesFacade],
  ['users', 'users', UserListPage, UsersFacade],
  ['parties', 'parties', PartyListPage, PartiesFacade],
])('list navigation: %s', (_name, section, page, facade) => {
  it(`opens a row at /${section}/<id>`, async () => {
    const routes: Routes = [
      {
        path: section,
        children: [
          {
            path: '',
            pathMatch: 'full',
            component: page as Type<{ onSelectionChanged?: unknown }>,
          },
          { path: ':id', component: DetailStubComponent },
        ],
      },
    ];
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: {} },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [provideRouter(routes), { provide: facade, useValue: empty }],
    });

    const harness = await RouterTestingHarness.create(`/${section}`);
    const list = harness.routeDebugElement?.componentInstance as {
      onSelectionChanged?(rows: { id: string }[]): void;
      open?(row: { id: string }): void;
    };
    // Migrated pages open a row through `nx-data-grid (rowOpen)` → `open(row)`.
    if (list.open) {
      list.open({ id: 'abc-123' });
    } else {
      list.onSelectionChanged?.([{ id: 'abc-123' }]);
    }
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe(`/${section}/abc-123`);
  });
});
