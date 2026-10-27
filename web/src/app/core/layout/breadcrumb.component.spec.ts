import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BreadcrumbComponent } from './breadcrumb.component';

@Component({ template: '', standalone: true })
class DummyComponent {}

const routes = [
  {
    path: 'catalog',
    data: { titleKey: 'nav.catalog' },
    children: [{ path: 'types', component: DummyComponent, data: { titleKey: 'nav.catalogTypes' } }],
  },
  { path: 'account', component: DummyComponent, data: { titleKey: 'nav.account' } },
];

describe('BreadcrumbComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes)],
    });
  });

  it('builds the crumb trail from the active route hierarchy titleKeys', async () => {
    await RouterTestingHarness.create('/catalog/types');

    const fixture = TestBed.createComponent(BreadcrumbComponent);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('nav.catalog');
    expect(text).toContain('nav.catalogTypes');
  });

  it('recomputes the crumb trail on every navigation', async () => {
    const harness = await RouterTestingHarness.create('/catalog/types');
    const fixture = TestBed.createComponent(BreadcrumbComponent);
    fixture.detectChanges();

    await harness.navigateByUrl('/account');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('nav.account');
    expect(text).not.toContain('nav.catalogTypes');
  });
});
