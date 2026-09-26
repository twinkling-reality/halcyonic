import { randomFillSync } from 'node:crypto';

export interface IdGenerator {
  next(): string;
}

export interface UuidV7Options {
  /** Milliseconds since the Unix epoch. */
  readonly now?: () => number;
  /** Fills the array with random bytes. Replace only for deterministic fixtures. */
  readonly random?: (bytes: Uint8Array) => void;
}

/**
 * RFC 9562 UUIDv7: 48-bit Unix milliseconds, then 74 random bits with the version and variant
 * set. The 12 bits after the version act as a counter within one millisecond, seeded randomly
 * in its lower half, so identifiers from one generator are strictly increasing even when many
 * are created in the same millisecond or the clock steps backwards.
 */
export function createUuidV7Generator(options: UuidV7Options = {}): IdGenerator {
  const now = options.now ?? Date.now;
  const fill = options.random ?? ((bytes: Uint8Array) => void randomFillSync(bytes));
  let lastMs = -1;
  let counter = 0;
  const random = new Uint8Array(10);

  return {
    next() {
      fill(random);
      let ms = now();
      if (ms <= lastMs) {
        ms = lastMs;
        counter += 1;
        if (counter > 0xfff) {
          ms += 1;
          counter = byte(random, 0) & 0x7ff;
        }
      } else {
        counter = ((byte(random, 0) << 3) | (byte(random, 1) >> 5)) & 0x7ff;
      }
      lastMs = ms;

      const bytes = new Uint8Array(16);
      const high = Math.floor(ms / 0x1000000);
      const low = ms % 0x1000000;
      bytes[0] = (high >>> 16) & 0xff;
      bytes[1] = (high >>> 8) & 0xff;
      bytes[2] = high & 0xff;
      bytes[3] = (low >>> 16) & 0xff;
      bytes[4] = (low >>> 8) & 0xff;
      bytes[5] = low & 0xff;
      bytes[6] = 0x70 | ((counter >>> 8) & 0x0f);
      bytes[7] = counter & 0xff;
      bytes[8] = 0x80 | (byte(random, 2) & 0x3f);
      for (let i = 9; i < 16; i += 1) bytes[i] = byte(random, i - 6);
      return format(bytes);
    },
  };
}

function byte(bytes: Uint8Array, index: number): number {
  return bytes[index] ?? 0;
}

function format(bytes: Uint8Array): string {
  const hex = Buffer.from(bytes).toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/**
 * Deterministic byte source (mulberry32) for reproducible fixtures. Never use it for anything
 * that needs to be unpredictable.
 */
export function createSeededRandom(seed: number): (bytes: Uint8Array) => void {
  let state = seed >>> 0;
  return (bytes) => {
    for (let i = 0; i < bytes.length; i += 1) {
      state = (state + 0x6d2b79f5) >>> 0;
      let t = state;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      bytes[i] = ((t ^ (t >>> 14)) >>> 0) & 0xff;
    }
  };
}
