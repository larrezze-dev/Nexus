import { Component, TemplateRef, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ShellComponent } from './shell.component';
import { NavigationBuilderService } from './navigation-builder.service';
import { NxNavItem } from './nav-item.model';

const fakeNavItems: NxNavItem[] = [{ titleKey: 'nav.account', path: '/account', icon: 'person' }];

@Component({
  standalone: true,
  imports: [ShellComponent],
  template: `
    <app-shell [userMenuExtra]="extra">
      <ng-template #extra><span data-testid="extra-marker">extra content</span></ng-template>
    </app-shell>
  `,
})
class HostComponent {
  @ViewChild(TemplateRef) extra!: TemplateRef<unknown>;
}

describe('ShellComponent', () => {
  let fixture: ComponentFixture<ShellComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [
        provideRouter([]),
        { provide: NavigationBuilderService, useValue: { build: () => fakeNavItems } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
  });

  it('renders the logo and a disabled, tooltipped search field', () => {
    const search: HTMLInputElement | null = fixture.nativeElement.querySelector('[data-testid="nx-shell-search"]');
    expect(fixture.nativeElement.textContent).toContain('NEXUS');
    expect(search).toBeTruthy();
    expect(search?.disabled).toBe(true);
    expect(search?.title).toBe('Próximamente');
  });

  it('does not render an approvals bell', () => {
    expect(fixture.nativeElement.querySelector('[aria-label="Approvals"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('notifications');
  });

  it('renders the user menu trigger', () => {
    expect(fixture.nativeElement.querySelector('[data-testid="nx-shell-user-menu-trigger"]')).toBeTruthy();
  });

  it('builds the side nav from NavigationBuilderService', () => {
    const links = fixture.nativeElement.querySelectorAll('mat-nav-list a');
    expect(links.length).toBe(1);
    expect(links[0].textContent).toContain('nav.account');
  });
});

describe('ShellComponent userMenuExtra projection', () => {
  it('projects the given template into the user menu via *ngTemplateOutlet', () => {
    const hostFixture = TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideRouter([]),
        { provide: NavigationBuilderService, useValue: { build: () => fakeNavItems } },
      ],
    }).createComponent(HostComponent);

    hostFixture.detectChanges();
    const shell = hostFixture.debugElement.children[0].componentInstance as ShellComponent;
    expect(shell.userMenuExtra).toBeTruthy();
  });
});
