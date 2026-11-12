import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxActionBarComponent } from './nx-action-bar.component';
import { NxFormGridComponent, NxSpanDirective } from './nx-form-grid.component';
import { NxFormSectionComponent } from './nx-form-section.component';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxFormSectionComponent, NxFormGridComponent, NxSpanDirective, NxActionBarComponent],
  template: `
    <nx-form-section
      id="identification"
      title="Identification"
      hint="The code cannot be changed later."
    >
      <nx-form-grid>
        <div nxSpan="s" class="f-s">code</div>
        <div nxSpan="m" class="f-m">country</div>
        <div nxSpan="l" class="f-l">name</div>
        <div nxSpan="xl" class="f-xl">notes</div>
        <div class="f-none">free</div>
      </nx-form-grid>
    </nx-form-section>
    <nx-action-bar><button type="button">Save</button></nx-action-bar>
  `,
})
class HostComponent {}

describe('NxFormSectionComponent + NxFormGridComponent + NxSpanDirective', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
  });

  it('renders the section title as h2 with its hint', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const section = (fixture.nativeElement as HTMLElement).querySelector(
      'nx-form-section',
    ) as HTMLElement;

    expect(section.querySelector('h2')?.textContent?.trim()).toBe('Identification');
    expect(section.querySelector('.nx-form-section__hint')?.textContent).toContain(
      'cannot be changed',
    );
    expect(section.id).toBe('identification');
  });

  it('marks each child with its span class (S/M/L/XL) inside a 12-column grid', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const grid = root.querySelector('nx-form-grid') as HTMLElement;

    expect(grid.classList).toContain('nx-form-grid');
    expect(root.querySelector('.f-s')?.classList).toContain('nx-span--s');
    expect(root.querySelector('.f-m')?.classList).toContain('nx-span--m');
    expect(root.querySelector('.f-l')?.classList).toContain('nx-span--l');
    expect(root.querySelector('.f-xl')?.classList).toContain('nx-span--xl');
    expect(root.querySelector('.f-none')?.className).not.toContain('nx-span--');
  });

  it('ships the 12 → 6 → 1 column rules and the span widths with the component', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const css = Array.from(document.querySelectorAll('style'))
      .map((style) => style.textContent ?? '')
      .join('\n');

    expect(css).toMatch(/\.nx-form-grid\s*\{[^}]*repeat\(12, minmax\(0, 1fr\)\)/);
    expect(css).toMatch(/max-width:\s*1279\.98px[\s\S]*repeat\(6, minmax\(0, 1fr\)\)/);
    expect(css).toMatch(/max-width:\s*599\.98px[\s\S]*grid-template-columns:\s*minmax\(0, 1fr\)/);
    expect(css).toMatch(/\.nx-span--s\s*\{\s*grid-column:\s*span 3/);
    expect(css).toMatch(/\.nx-span--m\s*\{\s*grid-column:\s*span 4/);
    expect(css).toMatch(/\.nx-span--l\s*\{\s*grid-column:\s*span 6/);
    expect(css).toMatch(/\.nx-span--xl\s*\{\s*grid-column:\s*span 12/);
    expect(css).toContain('var(--nx-form-gap-x)');
    expect(css).toContain('var(--nx-form-gap-y)');
  });

  it('projects the action bar buttons', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector(
      'nx-action-bar',
    ) as HTMLElement;

    expect(bar.classList).toContain('nx-action-bar');
    expect(bar.querySelector('button')?.textContent).toBe('Save');
  });
});
