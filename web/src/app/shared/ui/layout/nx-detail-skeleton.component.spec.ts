import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NxBreadcrumbService } from '../../../core/layout/breadcrumb.service';
import { NxDetailSkeletonComponent } from './nx-detail-skeleton.component';
import { layoutTransloco } from './testing';

describe('NxDetailSkeletonComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NxDetailSkeletonComponent, layoutTransloco()],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  function setup(tabs = false) {
    const fixture = TestBed.createComponent(NxDetailSkeletonComponent);
    fixture.componentRef.setInput('tabs', tabs);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('shows bars instead of a «Loading…» title, and tells screen readers once', () => {
    const { root } = setup();
    expect(root.querySelector('h1')).toBeNull();
    expect(root.querySelectorAll('.nx-skeleton-bar').length).toBeGreaterThan(3);
    const status = root.querySelector('[role="status"]');
    expect(status?.textContent?.trim()).toBe('Loading…');
    expect(root.querySelector('.nx-detail-skeleton__card')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('draws the field placeholders on the form grid and the tab bar only when asked', () => {
    const { root } = setup();
    expect(root.querySelectorAll('nx-form-grid .nx-detail-skeleton__field').length).toBe(5);
    expect(root.querySelector('[data-testid="nx-detail-skeleton-tabs"]')).toBeNull();
    expect(setup(true).root.querySelector('[data-testid="nx-detail-skeleton-tabs"]')).not.toBeNull();
  });

  it('owns the breadcrumb while it lives, like the page header', () => {
    const { fixture, root } = setup();
    const service = TestBed.inject(NxBreadcrumbService);
    expect(root.querySelector('app-breadcrumb')).not.toBeNull();
    expect(service.ownedByPage()).toBe(true);
    fixture.destroy();
    expect(service.ownedByPage()).toBe(false);
  });
});
