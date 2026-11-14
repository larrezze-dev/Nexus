import { IdempotentCreateId, uuidV7 } from './uuid-v7';

describe('uuidV7', () => {
  it('has the UUID shape with version 7 and the RFC variant', () => {
    expect(uuidV7()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });

  it('encodes the millisecond timestamp in the first 48 bits', () => {
    const now = Date.UTC(2026, 10, 14, 12, 0, 0);
    const hex = uuidV7(now).replace(/-/g, '').slice(0, 12);
    expect(parseInt(hex, 16)).toBe(now);
  });

  it('sorts by time and does not repeat', () => {
    const earlier = uuidV7(1_700_000_000_000);
    const later = uuidV7(1_700_000_000_001);
    expect(earlier < later).toBe(true);
    expect(uuidV7()).not.toBe(uuidV7());
  });
});

describe('IdempotentCreateId', () => {
  it('reuses the id for the same content and renews it for other content or after success', () => {
    const ids = new IdempotentCreateId();
    const first = ids.idFor({ name: 'A' });
    expect(ids.idFor({ name: 'A' })).toBe(first);
    const second = ids.idFor({ name: 'B' });
    expect(second).not.toBe(first);
    ids.clear();
    expect(ids.idFor({ name: 'B' })).not.toBe(second);
  });
});
