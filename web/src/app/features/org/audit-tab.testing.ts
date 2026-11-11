import { Component, Type, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { PermissionsStore } from '../../shared/auth/permissions.store';
import { NxAuditTrailComponent } from '../../shared/platform/nx-audit-trail/nx-audit-trail.component';

/** Same selector and inputs as the real `NxAuditTrailComponent`, without its HTTP facade. */
@Component({ selector: 'nx-audit-trail', template: '', host: { 'data-stub': 'audit-trail' } })
export class NxAuditTrailStubComponent {
  readonly aggregateType = input.required<string>();
  readonly aggregateId = input.required<string>();
}

/** Swaps the real audit trail for the stub in `page`'s imports. Call before `createComponent`. */
export function stubAuditTrail(page: Type<unknown>): void {
  TestBed.overrideComponent(page, {
    remove: { imports: [NxAuditTrailComponent] },
    add: { imports: [NxAuditTrailStubComponent] },
  });
}

/** Loads a permission list that does not include `AUDIT:READ` (an empty list means "allow all"). */
export function denyAuditRead(): void {
  TestBed.inject(PermissionsStore).setPermissions([{ code: 'ORG_STRUCTURE:READ', scopes: [] }]);
}

export function findAuditTab(fixture: ComponentFixture<unknown>): HTMLElement | undefined {
  const tabs = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]');
  return Array.from(tabs).find((tab) => (tab.textContent ?? '').includes('common.tabs.audit'));
}

/**
 * `MatTabGroup` renders tab bodies lazily, so the audit trail only exists once its tab is
 * selected. Clicks the tab and polls until the stub shows up, returning it (or `null`).
 */
export async function selectAuditTab(fixture: ComponentFixture<unknown>): Promise<NxAuditTrailStubComponent | null> {
  findAuditTab(fixture)?.click();
  fixture.detectChanges();
  for (let i = 0; i < 20; i++) {
    const found = fixture.debugElement.query(By.directive(NxAuditTrailStubComponent));
    if (found) {
      return found.componentInstance as NxAuditTrailStubComponent;
    }
    await Promise.resolve();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }
  return null;
}
