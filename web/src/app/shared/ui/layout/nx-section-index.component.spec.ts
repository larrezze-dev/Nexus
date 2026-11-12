import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxFormSectionComponent } from './nx-form-section.component';
import { NxSectionIndexComponent } from './nx-section-index.component';
import { layoutTransloco } from './testing';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxSectionIndexComponent, NxFormSectionComponent],
  template: `
    <nx-section-index [sections]="sections">
      <nx-form-section id="company" title="Company">a</nx-form-section>
      <nx-form-section id="tax" title="Tax data">b</nx-form-section>
    </nx-section-index>
  `,
})
class HostComponent {
  readonly sections = [
    { id: 'company', label: 'Company' },
    { id: 'tax', label: 'Tax data' },
  ];
}

describe('NxSectionIndexComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent, layoutTransloco()] }).compileComponents();
  });

  it('lists the sections as in-page links in a labelled nav next to the projected form', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const nav = root.querySelector('nav.nx-section-index__nav') as HTMLElement;
    expect(nav.getAttribute('aria-label')).toBe('On this page');
    const links = Array.from(nav.querySelectorAll('a'));
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Company', 'Tax data']);
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['#company', '#tax']);
    expect(links[0].getAttribute('aria-current')).toBe('location');
    expect(root.querySelector('.nx-section-index__content #tax')).toBeTruthy();
  });

  it('marks the clicked section as current and moves focus to its heading', async () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);

    (root.querySelectorAll('nav a')[1] as HTMLAnchorElement).click();
    fixture.detectChanges();

    const links = root.querySelectorAll('nav a');
    expect(links[1].getAttribute('aria-current')).toBe('location');
    expect(links[0].getAttribute('aria-current')).toBeNull();
    expect(document.activeElement?.textContent?.trim()).toBe('Tax data');
    root.remove();
  });
});
