import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { OrgNameCellComponent, OrgNameCellParams, secondaryName } from './org-name-cell.component';

interface Row {
  legalName: string;
  displayName: string | null;
}

function render(row: Row, secondary?: (r: Row | undefined) => string | null): HTMLElement {
  const fixture = TestBed.createComponent(OrgNameCellComponent);
  fixture.componentInstance.agInit({
    data: row,
    primary: (r: Row | undefined) => r?.legalName ?? '',
    secondary,
  } as unknown as OrgNameCellParams);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('OrgNameCellComponent', () => {
  it('draws the main line and the muted second line when there is one', () => {
    const el = render({ legalName: 'SAPE smart bud AG', displayName: 'SSB Schweiz' }, secondaryName);
    expect(el.querySelector('.nx-org-name')?.textContent).toBe('SAPE smart bud AG');
    expect(el.querySelector('.nx-org-name__secondary')?.textContent).toBe('SSB Schweiz');
  });

  it('draws no second line when it is empty or missing', () => {
    expect(render({ legalName: 'A', displayName: null }, secondaryName).querySelector('.nx-org-name__secondary')).toBeNull();
    expect(render({ legalName: 'A', displayName: 'x' }).querySelector('.nx-org-name__secondary')).toBeNull();
  });
});

describe('secondaryName', () => {
  it('shows the display name only when it is set and differs from the legal name', () => {
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: 'SSB Schweiz' })).toBe('SSB Schweiz');
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: 'SAPE smart bud AG' })).toBeNull();
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: null })).toBeNull();
    expect(secondaryName({ legalName: 'SAPE smart bud AG', displayName: '  ' })).toBeNull();
  });
});
