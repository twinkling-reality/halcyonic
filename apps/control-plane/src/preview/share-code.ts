/**
 * A share's address as a code to scan (ADR 0054): a QR code, encoded here with no dependency, in
 * byte mode at error correction level M (about 15% of the code may be lost and still read), the
 * smallest version that holds the address, from 1 to 13: a share's address is at most 253 bytes,
 * which version 12 holds. The headset draws the cells it is given and a light margin of
 * `QUIET_ZONE` cells around them; it never draws a link's address as text.
 */

/** The light cells a scanner needs around the code, on every side. */
export const QUIET_ZONE = 4;

/** The most bytes a code here holds: version 13 at level M. */
export const SHARE_CODE_MAX_BYTES = 331;

/**
 * Level M's blocks per version (ISO/IEC 18004 table 9): error correction codewords per block, then
 * each group's block count and data codewords per block.
 */
const LEVEL_M: readonly (readonly [number, ...(readonly [number, number])[]])[] = [
  [10, [1, 16]],
  [16, [1, 28]],
  [26, [1, 44]],
  [18, [2, 32]],
  [24, [2, 43]],
  [16, [4, 27]],
  [18, [4, 31]],
  [22, [2, 38], [2, 39]],
  [22, [3, 36], [2, 37]],
  [26, [4, 43], [1, 44]],
  [30, [1, 50], [4, 51]],
  [22, [6, 36], [2, 37]],
  [22, [8, 37], [1, 38]],
];

/** The centres of the alignment patterns per version, on both axes (table E.1). */
const ALIGNMENT: readonly (readonly number[])[] = [
  [],
  [6, 18],
  [6, 22],
  [6, 26],
  [6, 30],
  [6, 34],
  [6, 22, 38],
  [6, 24, 42],
  [6, 26, 46],
  [6, 28, 50],
  [6, 30, 54],
  [6, 32, 58],
  [6, 34, 62],
];

export interface ShareCodeCells {
  /** The cells on a side, quiet zone not included: 21 at version 1, 4 more per version. */
  readonly size: number;
  readonly version: number;
  /** Row by row, top first, each `size` characters: `1` a dark cell, `0` a light one. */
  readonly rows: readonly string[];
}

/** The code for `text` (UTF-8). Throws when it does not fit in a version 13 code. */
export function shareCode(text: string): ShareCodeCells {
  const bytes = new TextEncoder().encode(text);
  const version = versionFor(bytes.length);
  if (version === null) throw new Error('too long for a code to scan');
  const data = dataCodewords(bytes, version);
  const codewords = withErrorCorrection(data, version);
  const matrix = new Matrix(version);
  matrix.drawFunctionPatterns();
  matrix.drawCodewords(codewords);
  let best = 0;
  let lowest = Number.POSITIVE_INFINITY;
  for (let mask = 0; mask < 8; mask += 1) {
    matrix.applyMask(mask);
    matrix.drawFormat(mask);
    const penalty = matrix.penalty();
    if (penalty < lowest) {
      lowest = penalty;
      best = mask;
    }
    matrix.applyMask(mask);
  }
  matrix.applyMask(best);
  matrix.drawFormat(best);
  return { size: matrix.size, version, rows: matrix.rows() };
}

function versionFor(length: number): number | null {
  for (let version = 1; version <= LEVEL_M.length; version += 1) {
    const capacityBits = dataCount(version) * 8;
    if (4 + countBits(version) + length * 8 <= capacityBits) return version;
  }
  return null;
}

function countBits(version: number): number {
  return version <= 9 ? 8 : 16;
}

function blocksOf(version: number): { readonly ec: number; readonly lengths: number[] } {
  const [ec, ...groups] = LEVEL_M[version - 1] as readonly [number, ...[number, number][]];
  const lengths: number[] = [];
  for (const [count, length] of groups) for (let i = 0; i < count; i += 1) lengths.push(length);
  return { ec, lengths };
}

function dataCount(version: number): number {
  return blocksOf(version).lengths.reduce((sum, length) => sum + length, 0);
}

/** Mode, count, the bytes, the terminator and padding, as the data codewords. */
function dataCodewords(bytes: Uint8Array, version: number): number[] {
  const bits: number[] = [];
  const put = (value: number, length: number) => {
    for (let i = length - 1; i >= 0; i -= 1) bits.push((value >>> i) & 1);
  };
  put(0b0100, 4);
  put(bytes.length, countBits(version));
  for (const byte of bytes) put(byte, 8);
  const capacity = dataCount(version) * 8;
  put(0, Math.min(4, capacity - bits.length));
  while (bits.length % 8 !== 0) bits.push(0);
  const words: number[] = [];
  for (let i = 0; i < bits.length; i += 8)
    words.push(bits.slice(i, i + 8).reduce((word, bit) => (word << 1) | bit, 0));
  for (let pad = 0xec; words.length < capacity / 8; pad ^= 0xec ^ 0x11) words.push(pad);
  return words;
}

/** Each block's data with its Reed-Solomon codewords, interleaved block by block (8.6). */
function withErrorCorrection(data: number[], version: number): number[] {
  const { ec, lengths } = blocksOf(version);
  const divisor = rsDivisor(ec);
  const blocks: number[][] = [];
  const corrections: number[][] = [];
  let at = 0;
  for (const length of lengths) {
    const block = data.slice(at, at + length);
    at += length;
    blocks.push(block);
    corrections.push(rsRemainder(block, divisor));
  }
  const out: number[] = [];
  const longest = Math.max(...lengths);
  for (let i = 0; i < longest; i += 1)
    for (const block of blocks) if (i < block.length) out.push(block[i] as number);
  for (let i = 0; i < ec; i += 1) for (const block of corrections) out.push(block[i] as number);
  return out;
}

/** Multiplication in GF(2^8) modulo x^8 + x^4 + x^3 + x^2 + 1. */
function gfMultiply(x: number, y: number): number {
  let z = 0;
  for (let i = 7; i >= 0; i -= 1) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z & 0xff;
}

/** The generator polynomial of `degree`, its leading 1 left out, highest power first. */
function rsDivisor(degree: number): number[] {
  const result = new Array<number>(degree).fill(0);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i += 1) {
    for (let j = 0; j < degree; j += 1) {
      result[j] = gfMultiply(result[j] as number, root);
      if (j + 1 < degree) result[j] = (result[j] as number) ^ (result[j + 1] as number);
    }
    root = gfMultiply(root, 0x02);
  }
  return result;
}

function rsRemainder(data: readonly number[], divisor: readonly number[]): number[] {
  const result = new Array<number>(divisor.length).fill(0);
  for (const byte of data) {
    const factor = byte ^ (result.shift() as number);
    result.push(0);
    divisor.forEach((coefficient, i) => {
      result[i] = (result[i] as number) ^ gfMultiply(coefficient, factor);
    });
  }
  return result;
}

class Matrix {
  readonly version: number;
  readonly size: number;
  readonly #dark: boolean[][];
  readonly #fixed: boolean[][];

  constructor(version: number) {
    this.version = version;
    this.size = 17 + 4 * version;
    this.#dark = Array.from({ length: this.size }, () => new Array<boolean>(this.size).fill(false));
    this.#fixed = Array.from({ length: this.size }, () =>
      new Array<boolean>(this.size).fill(false),
    );
  }

  #set(x: number, y: number, dark: boolean): void {
    (this.#dark[y] as boolean[])[x] = dark;
    (this.#fixed[y] as boolean[])[x] = true;
  }

  drawFunctionPatterns(): void {
    for (let i = 0; i < this.size; i += 1) {
      this.#set(6, i, i % 2 === 0);
      this.#set(i, 6, i % 2 === 0);
    }
    this.#finder(3, 3);
    this.#finder(this.size - 4, 3);
    this.#finder(3, this.size - 4);
    const centres = ALIGNMENT[this.version - 1] as readonly number[];
    const last = centres.length - 1;
    for (let i = 0; i < centres.length; i += 1)
      for (let j = 0; j < centres.length; j += 1) {
        // The three corners the finders take have none.
        if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) continue;
        this.#alignment(centres[i] as number, centres[j] as number);
      }
    this.drawFormat(0);
    this.#versionInformation();
  }

  #finder(cx: number, cy: number): void {
    for (let dy = -4; dy <= 4; dy += 1)
      for (let dx = -4; dx <= 4; dx += 1) {
        const x = cx + dx;
        const y = cy + dy;
        if (x < 0 || y < 0 || x >= this.size || y >= this.size) continue;
        const distance = Math.max(Math.abs(dx), Math.abs(dy));
        this.#set(x, y, distance !== 2 && distance !== 4);
      }
  }

  #alignment(cx: number, cy: number): void {
    for (let dy = -2; dy <= 2; dy += 1)
      for (let dx = -2; dx <= 2; dx += 1)
        this.#set(cx + dx, cy + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
  }

  /** The format information, level M (00) and `mask`, in both its places, and the dark cell. */
  drawFormat(mask: number): void {
    const data = (0b00 << 3) | mask;
    let remainder = data;
    for (let i = 0; i < 10; i += 1) remainder = (remainder << 1) ^ ((remainder >>> 9) * 0x537);
    const bits = ((data << 10) | remainder) ^ 0x5412;
    const bit = (i: number) => ((bits >>> i) & 1) === 1;
    for (let i = 0; i <= 5; i += 1) this.#set(8, i, bit(i));
    this.#set(8, 7, bit(6));
    this.#set(8, 8, bit(7));
    this.#set(7, 8, bit(8));
    for (let i = 9; i < 15; i += 1) this.#set(14 - i, 8, bit(i));
    for (let i = 0; i < 8; i += 1) this.#set(this.size - 1 - i, 8, bit(i));
    for (let i = 8; i < 15; i += 1) this.#set(8, this.size - 15 + i, bit(i));
    this.#set(8, this.size - 8, true);
  }

  /** From version 7, the version in its two 6 by 3 places. */
  #versionInformation(): void {
    if (this.version < 7) return;
    let remainder = this.version;
    for (let i = 0; i < 12; i += 1) remainder = (remainder << 1) ^ ((remainder >>> 11) * 0x1f25);
    const bits = (this.version << 12) | remainder;
    for (let i = 0; i < 18; i += 1) {
      const dark = ((bits >>> i) & 1) === 1;
      const a = this.size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      this.#set(a, b, dark);
      this.#set(b, a, dark);
    }
  }

  /** The codewords in the zigzag of column pairs from the bottom right, past every fixed cell. */
  drawCodewords(codewords: readonly number[]): void {
    let i = 0;
    for (let right = this.size - 1; right >= 1; right -= 2) {
      if (right === 6) right = 5;
      for (let vertical = 0; vertical < this.size; vertical += 1)
        for (let j = 0; j < 2; j += 1) {
          const x = right - j;
          const upward = ((right + 1) & 2) === 0;
          const y = upward ? this.size - 1 - vertical : vertical;
          if ((this.#fixed[y] as boolean[])[x]) continue;
          if (i < codewords.length * 8) {
            const word = codewords[i >>> 3] as number;
            (this.#dark[y] as boolean[])[x] = ((word >>> (7 - (i & 7))) & 1) === 1;
            i += 1;
          }
        }
    }
  }

  /** Flips every cell `mask` names that is not fixed; applying it twice undoes it. */
  applyMask(mask: number): void {
    for (let y = 0; y < this.size; y += 1)
      for (let x = 0; x < this.size; x += 1) {
        if ((this.#fixed[y] as boolean[])[x]) continue;
        let flip: boolean;
        switch (mask) {
          case 0:
            flip = (x + y) % 2 === 0;
            break;
          case 1:
            flip = y % 2 === 0;
            break;
          case 2:
            flip = x % 3 === 0;
            break;
          case 3:
            flip = (x + y) % 3 === 0;
            break;
          case 4:
            flip = (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0;
            break;
          case 5:
            flip = ((x * y) % 2) + ((x * y) % 3) === 0;
            break;
          case 6:
            flip = (((x * y) % 2) + ((x * y) % 3)) % 2 === 0;
            break;
          default:
            flip = (((x + y) % 2) + ((x * y) % 3)) % 2 === 0;
        }
        if (flip) (this.#dark[y] as boolean[])[x] = !(this.#dark[y] as boolean[])[x];
      }
  }

  /** The four penalty rules of 8.8.2, lower being easier to read. */
  penalty(): number {
    const n = this.size;
    const at = (x: number, y: number) => (this.#dark[y] as boolean[])[x] as boolean;
    let score = 0;
    const lines: boolean[][] = [];
    for (let y = 0; y < n; y += 1) lines.push(Array.from({ length: n }, (_, x) => at(x, y)));
    for (let x = 0; x < n; x += 1) lines.push(Array.from({ length: n }, (_, y) => at(x, y)));
    const finderLike = [true, false, true, true, true, false, true];
    for (const line of lines) {
      // Rule 1: five or more of one colour in a row.
      let run = 1;
      for (let i = 1; i <= n; i += 1) {
        if (i < n && line[i] === line[i - 1]) run += 1;
        else {
          if (run >= 5) score += 3 + (run - 5);
          run = 1;
        }
      }
      // Rule 3: a finder's 1:1:3:1:1 with four light cells on either side.
      for (let i = 0; i + 7 <= n; i += 1) {
        if (!finderLike.every((dark, k) => line[i + k] === dark)) continue;
        const lightBefore = i >= 4 && [1, 2, 3, 4].every((k) => line[i - k] === false);
        const lightAfter = i + 11 <= n && [0, 1, 2, 3].every((k) => line[i + 7 + k] === false);
        if (lightBefore || lightAfter) score += 40;
      }
    }
    // Rule 2: each 2 by 2 block of one colour.
    for (let y = 0; y + 1 < n; y += 1)
      for (let x = 0; x + 1 < n; x += 1) {
        const c = at(x, y);
        if (c === at(x + 1, y) && c === at(x, y + 1) && c === at(x + 1, y + 1)) score += 3;
      }
    // Rule 4: how far the dark share is from half, in steps of 5%.
    let dark = 0;
    for (let y = 0; y < n; y += 1) for (let x = 0; x < n; x += 1) if (at(x, y)) dark += 1;
    score += Math.floor(Math.abs((dark * 20) / (n * n) - 10)) * 10;
    return score;
  }

  rows(): string[] {
    return this.#dark.map((row) => row.map((dark) => (dark ? '1' : '0')).join(''));
  }
}
