import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Translation, TranslocoService, provideTransloco, TranslocoLoader } from '@jsverse/transloco';
import { of } from 'rxjs';
import { NxBreadcrumbService } from './breadcrumb.service';
import { provideNexusTitleStrategy } from './page-title.strategy';

@Component({ template: '', standalone: true })
class DummyComponent {}

const routes: Routes = [
  { path: 'users', component: DummyComponent, data: { titleKey: 'nav.users' } },
  { path: 'users/new', component: DummyComponent, data: { listTitleKey: 'nav.users', crumbKey: 'breadcrumb.new.user' } },
  { path: 'users/:id', component: DummyComponent, data: { listTitleKey: 'nav.users', detail: true } },
  { path: 'boom', component: DummyComponent, data: { pageTitleKey: 'httpPages.notFound.title' } },
];

const translations: Record<string, Translation> = {
  en: {
    nav: { users: 'Users' },
    breadcrumb: { new: { user: 'New user' } },
    httpPages: { notFound: { title: 'Not found' } },
  },
  es: {
    nav: { users: 'Usuarios' },
    breadcrumb: { new: { user: 'Nuevo usuario' } },
    httpPages: { notFound: { title: 'No encontrada' } },
  },
};

class FakeLoader implements TranslocoLoader {
  getTranslation(lang: string) {
    return of(translations[lang] as Translation);
  }
}

describe('NxTitleStrategy', () => {
  let harness: RouterTestingHarness;
  let title: Title;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideNexusTitleStrategy(),
        provideTransloco({
          config: { availableLangs: ['en', 'es'], defaultLang: 'en', reRenderOnLangChange: true },
          loader: FakeLoader,
        }),
      ],
    });
    title = TestBed.inject(Title);
    harness = await RouterTestingHarness.create();
  });

  async function settle(): Promise<void> {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  it('sets "<page> · NEXUS" from the route data', async () => {
    await harness.navigateByUrl('/users');
    await settle();
    expect(title.getTitle()).toBe('Users · NEXUS');
  });

  it('uses crumbKey for "new" and pageTitleKey for title-only routes', async () => {
    await harness.navigateByUrl('/users/new');
    await settle();
    expect(title.getTitle()).toBe('New user · NEXUS');
    await harness.navigateByUrl('/boom');
    await settle();
    expect(title.getTitle()).toBe('Not found · NEXUS');
  });

  it('detail: list name first, record name once the page publishes it', async () => {
    await harness.navigateByUrl('/users/7');
    await settle();
    expect(title.getTitle()).toBe('Users · NEXUS');
    TestBed.inject(NxBreadcrumbService).setCurrentLabel('Jane Doe');
    await settle();
    expect(title.getTitle()).toBe('Jane Doe · NEXUS');
  });

  it('follows language changes', async () => {
    await harness.navigateByUrl('/users');
    await settle();
    TestBed.inject(TranslocoService).setActiveLang('es');
    await settle();
    expect(title.getTitle()).toBe('Usuarios · NEXUS');
  });
});
