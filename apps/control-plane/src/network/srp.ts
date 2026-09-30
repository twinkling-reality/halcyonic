import { createHash, randomBytes } from 'node:crypto';

/**
 * SRP-6a (RFC 5054 section 2), the password-authenticated key exchange a device proves the pairing
 * code with (ADR 0017). Each side learns the shared session key only if both used the same code; an
 * active attacker gets one guess per attempt and nothing to test offline. The device's side is in
 * C# (Halcyonic.Client.Srp6a); both are checked against RFC 5054's test vectors and each other.
 */
export interface SrpGroup {
  readonly N: bigint;
  readonly g: bigint;
  /** The length of N in bytes, which PAD() pads to. */
  readonly length: number;
  readonly hash: 'sha1' | 'sha256';
}

export function srpGroup(primeHex: string, g: bigint, hash: SrpGroup['hash']): SrpGroup {
  const N = BigInt(`0x${primeHex.replace(/\s+/g, '')}`);
  return { N, g, length: toBytes(N).length, hash };
}

/** RFC 5054 Appendix A, the 3072-bit group, with SHA-256. */
export const PAIRING_GROUP: SrpGroup = srpGroup(
  `FFFFFFFF FFFFFFFF C90FDAA2 2168C234 C4C6628B 80DC1CD1 29024E08
   8A67CC74 020BBEA6 3B139B22 514A0879 8E3404DD EF9519B3 CD3A431B
   302B0A6D F25F1437 4FE1356D 6D51C245 E485B576 625E7EC6 F44C42E9
   A637ED6B 0BFF5CB6 F406B7ED EE386BFB 5A899FA5 AE9F2411 7C4B1FE6
   49286651 ECE45B3D C2007CB8 A163BF05 98DA4836 1C55D39A 69163FA8
   FD24CF5F 83655D23 DCA3AD96 1C62F356 208552BB 9ED52907 7096966D
   670C354E 4ABC9804 F1746C08 CA18217C 32905E46 2E36CE3B E39E772C
   180E8603 9B2783A2 EC07A28F B5C55DF0 6F4C52C9 DE2BCBF6 95581718
   3995497C EA956AE5 15D22618 98FA0510 15728E5A 8AAAC42D AD33170D
   04507A33 A85521AB DF1CBA64 ECFB8504 58DBEF0A 8AEA7157 5D060C7D
   B3970F85 A6E1E4C7 ABF5AE8C DB0933D7 1E8C94E0 4A25619D CEE3D226
   1AD2EE6B F12FFA06 D98A0864 D8760273 3EC86A64 521F2B18 177B200C
   BBE11757 7A615D6C 770988C0 BAD946E2 08E24FA0 74E5AB31 43DB5BFC
   E0FD108E 4B82D120 A93AD2CA FFFFFFFF FFFFFFFF`,
  5n,
  'sha256',
);

/** The SRP user name for pairing. The code is the password; there is no account. */
export const PAIRING_IDENTITY = Buffer.from('halcyonic pairing', 'utf8');

/**
 * The control plane's side of one attempt. It holds the verifier, which a pairing window derives
 * from the code once, and a secret `b` of its own.
 */
export class SrpServer {
  readonly #group: SrpGroup;
  readonly #v: bigint;
  readonly #b: bigint;
  /** B = k * v + g^b mod N. */
  readonly B: bigint;

  /** `b` must be random and at least 256 bits; `create` draws it. */
  constructor(group: SrpGroup, v: bigint, b: bigint) {
    this.#group = group;
    this.#v = v;
    this.#b = b;
    this.B = serverPublic(group, v, b);
  }

  /**
   * A server for one attempt against a verifier, with a fresh 256-bit b and a B that is not 0 mod
   * N. Its work depends on b alone, never on the code.
   */
  static forVerifier(group: SrpGroup, v: bigint): SrpServer {
    for (;;) {
      const server = new SrpServer(group, v, fromBytes(randomBytes(32)));
      if (server.B !== 0n) return server;
    }
  }

  /** A server for one attempt, deriving the verifier from the password and salt first. */
  static create(group: SrpGroup, identity: Uint8Array, password: Uint8Array, salt: Uint8Array) {
    return SrpServer.forVerifier(group, verifier(group, identity, password, salt));
  }

  /**
   * The session key for the device's A, or null when A is invalid: RFC 5054 requires the server to
   * abort when A mod N is 0, which would give the same key whatever the password.
   */
  sessionKey(A: bigint): Buffer | null {
    const { N } = this.#group;
    if (A <= 0n || A >= N) return null;
    const u = scramble(this.#group, A, this.B);
    if (u === 0n) return null;
    return sessionKey(this.#group, serverSecret(this.#group, this.#v, A, this.#b, u));
  }
}

/** The device's side. The control plane uses it only in tests; the device's own is in C#. */
export class SrpClient {
  readonly #group: SrpGroup;
  readonly #identity: Uint8Array;
  readonly #password: Uint8Array;
  readonly #a: bigint;
  /** A = g^a mod N. */
  readonly A: bigint;

  constructor(group: SrpGroup, identity: Uint8Array, password: Uint8Array, a: bigint) {
    this.#group = group;
    this.#identity = identity;
    this.#password = password;
    this.#a = a;
    this.A = modPow(group.g, a, group.N);
  }

  /** The session key, or null when B is invalid (B mod N is 0) or the scrambler u is 0. */
  sessionKey(salt: Uint8Array, B: bigint): Buffer | null {
    if (B <= 0n || B >= this.#group.N) return null;
    const u = scramble(this.#group, this.A, B);
    if (u === 0n) return null;
    const x = privateKey(this.#group, this.#identity, this.#password, salt);
    return sessionKey(this.#group, clientSecret(this.#group, B, x, this.#a, u));
  }
}

/** k = H(N | PAD(g)). */
export function multiplier(group: SrpGroup): bigint {
  return fromBytes(hash(group, toBytes(group.N), pad(group, group.g)));
}

/** x = H(s | H(I | ":" | P)). */
export function privateKey(
  group: SrpGroup,
  identity: Uint8Array,
  password: Uint8Array,
  salt: Uint8Array,
): bigint {
  const inner = hash(group, identity, Buffer.from(':', 'utf8'), password);
  return fromBytes(hash(group, salt, inner));
}

/** v = g^x mod N. */
export function verifier(
  group: SrpGroup,
  identity: Uint8Array,
  password: Uint8Array,
  salt: Uint8Array,
): bigint {
  return modPow(group.g, privateKey(group, identity, password, salt), group.N);
}

/** B = (k * v + g^b) mod N. */
export function serverPublic(group: SrpGroup, v: bigint, b: bigint): bigint {
  return (multiplier(group) * v + modPow(group.g, b, group.N)) % group.N;
}

/** u = H(PAD(A) | PAD(B)). */
export function scramble(group: SrpGroup, A: bigint, B: bigint): bigint {
  return fromBytes(hash(group, pad(group, A), pad(group, B)));
}

/** The premaster secret S as the server computes it: (A * v^u)^b mod N. */
export function serverSecret(group: SrpGroup, v: bigint, A: bigint, b: bigint, u: bigint): bigint {
  const { N } = group;
  return modPow((A * modPow(v, u, N)) % N, b, N);
}

/** The premaster secret S as the client computes it: (B - k * g^x)^(a + u * x) mod N. */
export function clientSecret(group: SrpGroup, B: bigint, x: bigint, a: bigint, u: bigint): bigint {
  const { N, g } = group;
  const base = (((B - multiplier(group) * modPow(g, x, N)) % N) + N) % N;
  return modPow(base, a + u * x, N);
}

/** K = H(PAD(S)): both proofs and the credential's encryption are keyed with it. */
export function sessionKey(group: SrpGroup, S: bigint): Buffer {
  return hash(group, pad(group, S));
}

export function modPow(base: bigint, exponent: bigint, modulus: bigint): bigint {
  let result = 1n;
  let square = ((base % modulus) + modulus) % modulus;
  for (let rest = exponent; rest > 0n; rest >>= 1n) {
    if ((rest & 1n) === 1n) result = (result * square) % modulus;
    square = (square * square) % modulus;
  }
  return result;
}

/** The big-endian bytes of a non-negative integer, without leading zeros. */
export function toBytes(value: bigint): Buffer {
  if (value < 0n) throw new RangeError('SRP values are never negative');
  if (value === 0n) return Buffer.alloc(0);
  const hex = value.toString(16);
  return Buffer.from(hex.length % 2 === 0 ? hex : `0${hex}`, 'hex');
}

/** PAD(): the big-endian bytes, left-padded with zeros to the length of N. */
export function pad(group: SrpGroup, value: bigint): Buffer {
  const bytes = toBytes(value);
  if (bytes.length > group.length) throw new RangeError('the value is longer than N');
  return Buffer.concat([Buffer.alloc(group.length - bytes.length), bytes]);
}

export function fromBytes(bytes: Uint8Array): bigint {
  return bytes.length === 0 ? 0n : BigInt(`0x${Buffer.from(bytes).toString('hex')}`);
}

function hash(group: SrpGroup, ...parts: Uint8Array[]): Buffer {
  const digest = createHash(group.hash);
  for (const part of parts) digest.update(part);
  return digest.digest();
}
