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
});
