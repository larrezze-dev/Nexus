import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTransloco } from '@jsverse/transloco';
import { BreadcrumbComponent } from './breadcrumb.component';
import { NxBreadcrumbService } from './breadcrumb.service';

@Component({ template: '', standalone: true })
class DummyComponent {}

@Component({ template: '', standalone: true })
class RecordComponent {
  constructor() {
    inject(NxBreadcrumbService).setCurrentLabel('ACME AG');
  }
}

/** Same shapes as the real feature routes: componentless parent + flat siblings. */
const routes: Routes = [
  {
    path: 'legal-entities',
    children: [
      { path: '', pathMatch: 'full', component: DummyComponent, data: { navGroup: 'org', titleKey: 'nav.le' } },
      {
        path: 'new',
        component: DummyComponent,
        data: { navGroup: 'org', listTitleKey: 'nav.le', listPath: '/legal-entities', crumbKey: 'breadcrumb.new.le' },
      },
      {
        path: ':id',
        component: RecordComponent,
        data: { navGroup: 'org', listTitleKey: 'nav.le', listPath: '/legal-entities', detail: true },
      },
    ],
  },
  { path: 'locations', component: DummyComponent, data: { navGroup: 'org', titleKey: 'nav.loc' } },
  {
    path: 'locations/new',
    component: DummyComponent,
    data: { navGroup: 'org', listTitleKey: 'nav.loc', listPath: '/locations', crumbKey: 'breadcrumb.new.loc' },
  },
  {
    path: 'locations/:id',
    component: DummyComponent,
    data: { navGroup: 'org', listTitleKey: 'nav.loc', listPath: '/locations', detail: true },
  },
  { path: 'account', component: DummyComponent, data: { titleKey: 'nav.account' } },
];

describe('BreadcrumbComponent', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideTransloco({ config: { availableLangs: ['de', 'en', 'fr', 'it', 'es'], defaultLang: 'de' } }),
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function crumbsAt(url: string): Promise<string[]> {
    await harness.navigateByUrl(url);
    const fixture = TestBed.createComponent(BreadcrumbComponent);
    fixture.detectChanges();
    const items = Array.from(fixture.nativeElement.querySelectorAll('.nx-breadcrumb__item')) as HTMLElement[];
    return items.map((item) => item.textContent!.trim());
  }

  it('list on a componentless parent: group › list, no duplicate crumb', async () => {
    expect(await crumbsAt('/legal-entities')).toEqual(['nav.groups.org', 'nav.le']);
  });

  it('new on a componentless parent: group › list › new', async () => {
    expect(await crumbsAt('/legal-entities/new')).toEqual(['nav.groups.org', 'nav.le', 'breadcrumb.new.le']);
  });

  it('detail shows the record name published by the page', async () => {
    expect(await crumbsAt('/legal-entities/123')).toEqual(['nav.groups.org', 'nav.le', 'ACME AG']);
  });

  it('detail shows a neutral placeholder until the page publishes a label, and clears it on navigation', async () => {
    expect(await crumbsAt('/locations/9')).toEqual(['nav.groups.org', 'nav.loc', 'breadcrumb.loading']);
    TestBed.inject(NxBreadcrumbService).setCurrentLabel('Zurich');
    expect(await crumbsAt('/locations/9')).toEqual(['nav.groups.org', 'nav.loc', 'Zurich']);
    await crumbsAt('/locations');
    expect(await crumbsAt('/locations/10')).toEqual(['nav.groups.org', 'nav.loc', 'breadcrumb.loading']);
  });

  it('flat routes: list and new have a trail (no empty breadcrumb)', async () => {
    expect(await crumbsAt('/locations')).toEqual(['nav.groups.org', 'nav.loc']);
    expect(await crumbsAt('/locations/new')).toEqual(['nav.groups.org', 'nav.loc', 'breadcrumb.new.loc']);
  });

  it('only the list crumb is a link; group and current page are plain text', async () => {
    await harness.navigateByUrl('/legal-entities/new');
    const fixture = TestBed.createComponent(BreadcrumbComponent);
    fixture.detectChanges();
    const links = Array.from(fixture.nativeElement.querySelectorAll('a')) as HTMLAnchorElement[];
    expect(links.map((a) => a.textContent!.trim())).toEqual(['nav.le']);
    expect(links[0].getAttribute('href')).toBe('/legal-entities');
    expect(fixture.nativeElement.querySelector('[aria-current="page"]').textContent.trim()).toBe('breadcrumb.new.le');
  });

  it('account page: just the page name', async () => {
    expect(await crumbsAt('/account')).toEqual(['nav.account']);
  });
});
