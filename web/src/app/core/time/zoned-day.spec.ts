import { describe, expect, it } from 'vitest';
import { endOfZonedDay, startOfZonedDay } from './zoned-day';

describe('zoned day', () => {
  it('end of a winter day in Zurich is 22:59:59.999Z (UTC+1)', () => {
    expect(endOfZonedDay('2026-01-15', 'Europe/Zurich').toISOString()).toBe('2026-01-15T22:59:59.999Z');
  });

  it('end of a summer day in Zurich is 21:59:59.999Z (UTC+2)', () => {
    expect(endOfZonedDay('2026-07-15', 'Europe/Zurich').toISOString()).toBe('2026-07-15T21:59:59.999Z');
  });

  it('handles the DST change days and month/year rollover', () => {
    expect(endOfZonedDay('2026-03-29', 'Europe/Zurich').toISOString()).toBe('2026-03-29T21:59:59.999Z');
    expect(endOfZonedDay('2026-10-25', 'Europe/Zurich').toISOString()).toBe('2026-10-25T22:59:59.999Z');
    expect(endOfZonedDay('2026-12-31', 'Europe/Zurich').toISOString()).toBe('2026-12-31T22:59:59.999Z');
  });

  it('start of day', () => {
    expect(startOfZonedDay(2026, 11, 14, 'Europe/Zurich').toISOString()).toBe('2026-11-13T23:00:00.000Z');
  });
});
