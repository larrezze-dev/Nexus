import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxFilterBarComponent } from './nx-filter-bar.component';
import { layoutTransloco } from './testing';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxFilterBarComponent],
  template: `
    <nx-filter-bar [showClear]="showClear()" (clear)="cleared = cleared + 1">
      <input class="test-filter" aria-label="Search" />
    </nx-filter-bar>
  `,
})
class HostComponent {
  readonly showClear = signal(true);
  cleared = 0;
}

describe('NxFilterBarComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent, layoutTransloco()] }).compileComponents();
  });

  it('is a labelled search landmark with the projected filters and a translated «Clear filters» that emits clear', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('nx-filter-bar') as HTMLElement;

    expect(bar.getAttribute('role')).toBe('search');
    expect(bar.getAttribute('aria-label')).toBe('Filters');
    expect(bar.querySelector('.test-filter')).toBeTruthy();
    const clear = bar.querySelector('[data-testid="nx-filter-bar-clear"]') as HTMLButtonElement;
    expect(clear.textContent?.trim()).toBe('Clear filters');

    clear.click();
    expect(fixture.componentInstance.cleared).toBe(1);
  });

  it('hides «Clear filters» when showClear is false', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.showClear.set(false);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="nx-filter-bar-clear"]')).toBeNull();
  });
});
