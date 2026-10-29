import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { ICellRendererParams } from 'ag-grid-community';
import { OrgActiveStatusCellComponent } from './org-active-status-cell.component';

function paramsFor(value: boolean): ICellRendererParams<unknown, boolean> {
  return { value } as unknown as ICellRendererParams<unknown, boolean>;
}

describe('OrgActiveStatusCellComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        OrgActiveStatusCellComponent,
        TranslocoTestingModule.forRoot({
          langs: { en: { shared: { status: { active: 'Active', inactive: 'Inactive' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }).compileComponents();
  });

  it('shows "Active" when the row is active and "Inactive" after refresh toggles it off', () => {
    const fixture = TestBed.createComponent(OrgActiveStatusCellComponent);
    fixture.componentInstance.agInit(paramsFor(true));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Active');

    const refreshed = fixture.componentInstance.refresh(paramsFor(false));
    fixture.detectChanges();

    expect(refreshed).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Inactive');
  });
});
