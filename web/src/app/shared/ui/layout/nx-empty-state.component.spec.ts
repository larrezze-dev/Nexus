import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NxEmptyStateComponent, NxEmptyStateKind } from './nx-empty-state.component';
import { layoutTransloco } from './testing';

@Component({
  selector: 'nx-test-host',
  standalone: true,
  imports: [NxEmptyStateComponent],
  template: `
    <nx-empty-state [headingLevel]="level()" [kind]="kind()" [title]="title()" [message]="message()" [icon]="icon()">
      <button type="button" class="test-action">Retry</button>
    </nx-empty-state>
  `,
})
class HostComponent {
  readonly level = signal<1 | 2>(2);
  readonly kind = signal<NxEmptyStateKind>('empty');
  readonly title = signal<string | undefined>(undefined);
  readonly message = signal<string | undefined>(undefined);
  readonly icon = signal<string | undefined>(undefined);
}

describe('NxEmptyStateComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent, layoutTransloco()],
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const state = () =>
      (fixture.nativeElement as HTMLElement).querySelector('nx-empty-state') as HTMLElement;
    return { fixture, state };
  }

  it.each<[NxEmptyStateKind, string, string]>([
    ['empty', 'inbox', 'No records yet'],
    ['no-results', 'filter_alt_off', 'No records match'],
    ['error', 'cloud_off', 'Could not load'],
    ['forbidden', 'lock', 'No access'],
    ['not-found', 'search_off', 'Not found'],
  ])('kind %s shows the %s icon and a generic title', (kind, icon, title) => {
    const { fixture, state } = setup();
    fixture.componentInstance.kind.set(kind);
    fixture.detectChanges();

    expect(state().querySelector('mat-icon')?.textContent?.trim()).toBe(icon);
    expect(state().querySelector('.nx-empty-state__icon')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
    expect(state().querySelector('h2')?.textContent?.trim()).toBe(title);
    expect(state().classList).toContain(`nx-empty-state--${kind}`);
  });

  it('announces only the error kind as an alert', () => {
    const { fixture, state } = setup();
    expect(state().getAttribute('role')).toBeNull();

    fixture.componentInstance.kind.set('error');
    fixture.detectChanges();
    expect(state().getAttribute('role')).toBe('alert');
  });

  it('uses the caller title, message and icon, projects the action, and hides the message when it is an empty string', () => {
    const { fixture, state } = setup();
    fixture.componentInstance.title.set('No warehouses yet');
    fixture.componentInstance.message.set('Create the first one.');
    fixture.componentInstance.icon.set('warehouse');
    fixture.detectChanges();

    expect(state().querySelector('h2')?.textContent?.trim()).toBe('No warehouses yet');
    expect(state().querySelector('.nx-empty-state__message')?.textContent?.trim()).toBe(
      'Create the first one.',
    );
    expect(state().querySelector('mat-icon')?.textContent?.trim()).toBe('warehouse');
    expect(state().querySelector('.nx-empty-state__actions .test-action')).toBeTruthy();

    fixture.componentInstance.message.set('');
    fixture.detectChanges();
    expect(state().querySelector('.nx-empty-state__message')).toBeNull();
  });

  it('renders the title as h2 by default and as h1 with headingLevel 1, same class', () => {
    const { fixture, state } = setup();
    expect(state().querySelector('h2.nx-empty-state__title')).toBeTruthy();
    expect(state().querySelector('h1')).toBeNull();

    fixture.componentInstance.level.set(1);
    fixture.detectChanges();
    expect(state().querySelector('h1.nx-empty-state__title')).toBeTruthy();
    expect(state().querySelector('h2')).toBeNull();
  });
});
