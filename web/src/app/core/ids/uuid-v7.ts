/**
 * Generates a UUIDv7 (RFC 9562) in the browser: 48-bit Unix millisecond timestamp, version 7,
 * variant `10`, and the rest random (`crypto.getRandomValues`). The client sends it as the optional
 * `id` of a `POST` so the create is idempotent (conventions-api.md §5, ADR-0009).
 *
 * @param now milliseconds since the Unix epoch; only tests pass it.
 */
export function uuidV7(now: number = Date.now()): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);

  // Big-endian 48-bit timestamp (`>>> 0` / division avoid 32-bit overflow of `<<`).
  const high = Math.floor(now / 2 ** 16);
  bytes[0] = (high / 2 ** 24) & 0xff;
  bytes[1] = (high / 2 ** 16) & 0xff;
  bytes[2] = (high / 2 ** 8) & 0xff;
  bytes[3] = high & 0xff;
  bytes[4] = (now / 2 ** 8) & 0xff;
  bytes[5] = now & 0xff;

  bytes[6] = (bytes[6] & 0x0f) | 0x70; // version 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Remembers the `id` of a create that may or may not have reached the server, so a retry of the
 * SAME content reuses it (the server answers 200 with the existing record instead of creating a
 * duplicate) while changed content gets a fresh `id` (the same `id` with other content is a 409).
 */
export class IdempotentCreateId {
  private pending: { id: string; fingerprint: string } | null = null;

  /** The id to send for `body`: the remembered one if `body` is unchanged, else a new one. */
  idFor(body: unknown): string {
    const fingerprint = JSON.stringify(body);
    if (!this.pending || this.pending.fingerprint !== fingerprint) {
      this.pending = { id: uuidV7(), fingerprint };
    }
    return this.pending.id;
  }

  /** Call once the create succeeded: the next create is a new record. */
  clear(): void {
    this.pending = null;
  }
}
