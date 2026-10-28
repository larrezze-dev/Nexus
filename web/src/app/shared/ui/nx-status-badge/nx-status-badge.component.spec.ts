import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { NxStatusBadgeComponent } from './nx-status-badge.component';

describe('NxStatusBadgeComponent', () => {
  let fixture: ComponentFixture<NxStatusBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        NxStatusBadgeComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              shared: {
                status: {
                  active: 'Active',
                  custom: 'Custom',
                  unknown_status: 'unknown_status',
                },
              },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(NxStatusBadgeComponent);
  });

  it('renders the default mapping for a known status', () => {
    fixture.componentRef.setInput('status', 'active');
    fixture.detectChanges();

    const chip = fixture.nativeElement.querySelector('mat-chip');
    expect(chip.textContent).toContain('Active');
    expect(chip.classList).toContain('nx-status-badge--success');
  });

  it('falls back to a neutral badge with a generated i18n key for an unknown status', () => {
    fixture.componentRef.setInput('status', 'unknown_status');
    fixture.detectChanges();

    const chip = fixture.nativeElement.querySelector('mat-chip');
    expect(chip.classList).toContain('nx-status-badge--neutral');
    expect(chip.textContent).toContain('unknown_status');
  });

  it('uses the statusConfig input when provided', () => {
    fixture.componentRef.setInput('status', 'custom');
    fixture.componentRef.setInput('statusConfig', {
      custom: { colorClass: 'nx-status-badge--warning', i18nKey: 'shared.status.custom' },
    });
    fixture.detectChanges();

    const chip = fixture.nativeElement.querySelector('mat-chip');
    expect(chip.classList).toContain('nx-status-badge--warning');
    expect(chip.textContent).toContain('Custom');
  });
});
