import { computeAuditDiff, flattenPayload, isUuid, shortUuid } from './audit-diff';

describe('audit-diff', () => {
  it('flattens nested maps with dot paths and ignores id', () => {
    const flat = flattenPayload({ id: 'x', code: 'A', captionI18n: { de: 'Lager', en: 'Warehouse' } });
    expect([...flat.keys()]).toEqual(['code', 'captionI18n.de', 'captionI18n.en']);
  });

  it('returns only the changed sub-key of a nested map', () => {
    const diff = computeAuditDiff(
      { id: '1', code: 'A', captionI18n: { de: 'Neu', en: 'Same' } },
      { id: '1', code: 'A', captionI18n: { de: 'Alt', en: 'Same' } },
    );
    expect(diff).toEqual({
      kind: 'changes',
      rows: [{ path: 'captionI18n.de', field: 'captionI18n', subKey: 'de', before: 'Alt', after: 'Neu' }],
    });
  });

  it('lists every field as initial values when there is no previous payload', () => {
    const diff = computeAuditDiff({ id: '1', code: 'A', isActive: true }, null);
    expect(diff.kind).toBe('initial');
    if (diff.kind === 'initial') {
      expect(diff.rows.map((r) => r.path)).toEqual(['code', 'isActive']);
      expect(diff.rows[1].after).toBe(true);
    }
  });

  it('returns pii when the payload is null', () => {
    expect(computeAuditDiff(null, null)).toEqual({ kind: 'pii' });
    expect(computeAuditDiff(null, { code: 'A' })).toEqual({ kind: 'pii' });
  });

  it('returns no rows when nothing changed', () => {
    expect(computeAuditDiff({ code: 'A', m: { de: 'x' } }, { code: 'A', m: { de: 'x' } })).toEqual({
      kind: 'changes',
      rows: [],
    });
  });

  it('handles null to value and value to null (added and removed fields)', () => {
    const diff = computeAuditDiff(
      { businessUnitId: 'bu', gone: null },
      { businessUnitId: null, gone: 'was' },
    );
    expect(diff.kind === 'changes' && diff.rows).toEqual([
      { path: 'businessUnitId', field: 'businessUnitId', subKey: null, before: null, after: 'bu' },
      { path: 'gone', field: 'gone', subKey: null, before: 'was', after: null },
    ]);
  });

  it('treats an id-only change as no change', () => {
    expect(computeAuditDiff({ id: 'b', code: 'A' }, { id: 'a', code: 'A' })).toEqual({ kind: 'changes', rows: [] });
  });

  it('shortens uuids to first 8 + last 4', () => {
    const id = '0192f3a1-7e3b-7c5a-9c2e-6a2f8e9b0c7c';
    expect(isUuid(id)).toBe(true);
    expect(shortUuid(id)).toBe('0192f3a1…0c7c');
    expect(isUuid('nope')).toBe(false);
  });

  it('shows a removed map key as value to empty when the map becomes {}', () => {
    expect(computeAuditDiff({ captionI18n: {} }, { captionI18n: { de: 'x' } })).toEqual({
      kind: 'changes',
      rows: [{ path: 'captionI18n.de', field: 'captionI18n', subKey: 'de', before: 'x', after: null }],
    });
  });

  it('returns no rows for {} versus {}', () => {
    expect(computeAuditDiff({}, {})).toEqual({ kind: 'changes', rows: [] });
  });
});
