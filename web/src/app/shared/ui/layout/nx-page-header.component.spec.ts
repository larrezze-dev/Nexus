import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NxPageHeaderComponent } from './nx-page-header.component';
import { NxPageComponent } from './nx-page.component';
import { layoutTransloco } from './testing';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxPageComponent, NxPageHeaderComponent],
  template: `
    <nx-page [width]="width()">
      <nx-page-header [title]="title()" [subtitle]="subtitle()" [count]="count()" [breadcrumb]="breadcrumb()">
        <span nxHeaderStatus class="test-status">Active</span>
        <button nxHeaderActions type="button" class="test-action">New</button>
      </nx-page-header>
    </nx-page>
  `,
})
class HostComponent {
  readonly width = signal<'full' | 'form'>('full');
  readonly title = signal('Warehouses');
  readonly subtitle = signal<string | undefined>(undefined);
  readonly count = signal<number | undefined>(undefined);
  readonly breadcrumb = signal(true);
}

describe('NxPageComponent + NxPageHeaderComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent, layoutTransloco()],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('does not render the breadcrumb by default (the shell does, until PR-5)', () => {
    const { fixture, root } = setup();
    fixture.componentInstance.breadcrumb.set(false);
    fixture.detectChanges();
    expect(root.querySelector('nx-page-header app-breadcrumb')).toBeNull();
  });

  it('renders the breadcrumb above a single h1 with the title, the status slot next to it and the actions slot', () => {
    const { root } = setup();
    const header = root.querySelector('nx-page-header') as HTMLElement;

    expect(header.firstElementChild?.tagName.toLowerCase()).toBe('app-breadcrumb');
    expect(root.querySelectorAll('h1').length).toBe(1);
    expect(header.querySelector('h1')?.textContent?.trim()).toBe('Warehouses');
    expect(header.querySelector('.nx-page-header__title-line .test-status')).toBeTruthy();
    expect(header.querySelector('.nx-page-header__actions .test-action')).toBeTruthy();
  });

  it('shows the subtitle and the translated record count only when given', () => {
    const { fixture, root } = setup();
    expect(root.querySelector('.nx-page-header__subtitle')).toBeNull();
    expect(root.querySelector('[data-testid="nx-page-header-count"]')).toBeNull();

    fixture.componentInstance.subtitle.set('WH-BCN-01 · modified 11.11.2026');
    fixture.componentInstance.count.set(4);
    fixture.detectChanges();
    expect(root.querySelector('.nx-page-header__subtitle')?.textContent).toContain('WH-BCN-01');
    expect(root.querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe(
      '4 records',
    );

    fixture.componentInstance.count.set(1);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe(
      '1 record',
    );

    fixture.componentInstance.count.set(0);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="nx-page-header-count"]')?.textContent?.trim()).toBe(
      '0 records',
    );
  });

  it('switches the page between full width and the form width', () => {
    const { fixture, root } = setup();
    const page = root.querySelector('nx-page') as HTMLElement;
    expect(page.classList).toContain('nx-page--full');

    fixture.componentInstance.width.set('form');
    fixture.detectChanges();
    expect(page.classList).toContain('nx-page--form');
    expect(page.classList).not.toContain('nx-page--full');
  });
});
