import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxFormSectionComponent } from './nx-form-section.component';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxFormSectionComponent],
  template: `
    <nx-form-section title="Assignments" hint="Roles and where they apply">
      <a nxSectionActions href="/x" class="test-link">What does this user see?</a>
      <p class="test-body">Body</p>
    </nx-form-section>
    <nx-form-section title="Plain"><p>Body</p></nx-form-section>
  `,
})
class HostComponent {}

@Component({
  selector: 'nx-test-collapsible-host',
  standalone: true,
  imports: [NxFormSectionComponent],
  template: `
    <nx-form-section title="Advanced" hint="Technical data" [collapsible]="true" [(expanded)]="open">
      <p class="test-body">Body</p>
    </nx-form-section>
  `,
})
class CollapsibleHostComponent {
  open = false;
}

describe('NxFormSectionComponent', () => {
  it('renders the h2 title, the hint, the actions in the header and the body below', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const [withActions, plain] = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('nx-form-section'),
    );

    expect(withActions.querySelector('h2')?.textContent).toBe('Assignments');
    expect(withActions.querySelector('.nx-form-section__hint')?.textContent).toBe('Roles and where they apply');
    expect(withActions.querySelector('.nx-form-section__actions .test-link')).not.toBeNull();
    expect(withActions.querySelector('.nx-form-section__header .test-body')).toBeNull();
    expect(plain.querySelector('.nx-form-section__actions')?.children.length).toBe(0);
  });

  it('collapsible: a disclosure button in the h2, closed by default, the hint visible, the body hidden', () => {
    const fixture = TestBed.createComponent(CollapsibleHostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = el.querySelector<HTMLButtonElement>('h2 button.nx-form-section__toggle')!;
    const body = el.querySelector<HTMLElement>('.nx-form-section__body')!;

    expect(button.textContent).toContain('Advanced');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.getAttribute('aria-controls')).toBe(body.id);
    expect(body.hidden).toBe(true);
    expect(el.querySelector('.nx-form-section__hint')?.textContent).toBe('Technical data');

    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(body.hidden).toBe(false);
    expect(fixture.componentInstance.open).toBe(true);
  });

  it('collapsible: the page can open it through [(expanded)]', () => {
    const fixture = TestBed.createComponent(CollapsibleHostComponent);
    fixture.componentInstance.open = true;
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.nx-form-section__toggle')?.getAttribute('aria-expanded')).toBe('true');
    expect(el.querySelector<HTMLElement>('.nx-form-section__body')!.hidden).toBe(false);
  });

  it('plain sections keep the body visible and have no toggle', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.nx-form-section__toggle')).toBeNull();
    expect(Array.from(el.querySelectorAll<HTMLElement>('.nx-form-section__body')).every((b) => !b.hidden)).toBe(true);
  });
});
