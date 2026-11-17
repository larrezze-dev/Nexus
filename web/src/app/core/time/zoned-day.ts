/** UTC offset (ms) of `timeZone` at the instant `utcMs` (positive east of Greenwich). */
function zoneOffsetMs(utcMs: number, timeZone: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: 'numeric',
    minute: 'numeric',
    second: 'numeric',
  }).formatToParts(new Date(utcMs));
  const get = (type: string) => Number(parts.find((part) => part.type === type)?.value);
  const asUtc = Date.UTC(get('year'), get('month') - 1, get('day'), get('hour'), get('minute'), get('second'));
  return asUtc - Math.floor(utcMs / 1000) * 1000;
}

/** The instant at which the calendar day `year-month-day` (month 1-12) starts in `timeZone`. */
export function startOfZonedDay(year: number, month: number, day: number, timeZone: string): Date {
  const guess = Date.UTC(year, month - 1, day);
  const first = guess - zoneOffsetMs(guess, timeZone);
  return new Date(guess - zoneOffsetMs(first, timeZone));
}

/**
 * The last millisecond of the calendar day `YYYY-MM-DD` in `timeZone` (start of the next day minus 1 ms).
 * Querying "as of day X" at this instant shows every change made during X, since validity windows are
 * half-open (`valid_from <= t < valid_to`).
 */
export function endOfZonedDay(ymd: string, timeZone: string): Date {
  const [year, month, day] = ymd.split('-').map(Number);
  const nextDay = new Date(Date.UTC(year, month - 1, day + 1));
  const start = startOfZonedDay(nextDay.getUTCFullYear(), nextDay.getUTCMonth() + 1, nextDay.getUTCDate(), timeZone);
  return new Date(start.getTime() - 1);
}
