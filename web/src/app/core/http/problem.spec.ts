import { isConcurrencyConflict, isDuplicateConflict, localizedFieldErrors, problemOf } from './problem';

const conflictType = 'https://nexus.nussli.ch/problems/conflict';

describe('problem helpers', () => {
  it('problemOf reads the Problem body of an HttpErrorResponse-like error', () => {
    expect(problemOf({ status: 409, error: { code: 'CONFLICT' } })?.code).toBe('CONFLICT');
    expect(problemOf({ status: 500 })).toBeUndefined();
    expect(problemOf(null)).toBeUndefined();
  });

  it('isConcurrencyConflict is true for the real backend Problem (type .../conflict + code CONCURRENCY)', () => {
    expect(isConcurrencyConflict({ status: 409, error: { type: conflictType, code: 'CONCURRENCY' } })).toBe(true);
  });

  it('isConcurrencyConflict still accepts the legacy type "concurrency"', () => {
    expect(isConcurrencyConflict({ status: 409, error: { type: 'concurrency' } })).toBe(true);
  });

  it('a duplicate-key CONFLICT is not a concurrency conflict, and the other way round', () => {
    const duplicate = { status: 409, error: { type: conflictType, code: 'CONFLICT' } };
    const stale = { status: 409, error: { type: conflictType, code: 'CONCURRENCY' } };
    expect(isConcurrencyConflict(duplicate)).toBe(false);
    expect(isDuplicateConflict(duplicate)).toBe(true);
    expect(isDuplicateConflict(stale)).toBe(false);
  });

  it('neither matches without a 409', () => {
    expect(isConcurrencyConflict({ status: 422, error: { code: 'CONCURRENCY' } })).toBe(false);
    expect(isDuplicateConflict({ status: 400, error: { code: 'CONFLICT' } })).toBe(false);
  });

  it('localizedFieldErrors replaces every server message with the translated one', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const result = localizedFieldErrors({ code: ['must not be empty'], name: ['too long', 'x'] }, 'Invalid value');

    expect(result).toEqual({ code: ['Invalid value'], name: ['Invalid value'] });
    // The English detail only goes to the console.
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});
