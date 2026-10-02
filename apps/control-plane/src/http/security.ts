import { createHash, createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { chmod, readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

export const ACCESS_TOKEN_FILE = 'access-token';

/**
 * The single local credential, stored in the data directory with owner-only permissions.
 * Anyone who can read it can act as the owner, so it is never logged. It is an interim
 * mechanism until device pairing exists (see docs/internal/architecture/SECURITY.md).
 */
export async function loadOrCreateAccessToken(
  dataDir: string,
): Promise<{ token: string; path: string; created: boolean }> {
  const path = join(dataDir, ACCESS_TOKEN_FILE);
  try {
    const existing = (await readFile(path, 'utf8')).trim();
    if (existing.length < 32) {
      throw new Error(
        `${path} does not contain a valid access token; delete it to create a new one`,
      );
    }
    await chmod(path, 0o600);
    return { token: existing, path, created: false };
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
  }
  const token = randomBytes(32).toString('base64url');
  await writeFile(path, `${token}\n`, { mode: 0o600, flag: 'wx' });
  return { token, path, created: true };
}

export interface RequestFacts {
  readonly host: string | undefined;
  readonly origin: string | undefined;
  readonly secFetchSite: string | undefined;
  readonly authorization: string | undefined;
  readonly path: string;
  readonly localPort: number;
}

export type GuardDecision =
  | { readonly allowed: true }
  | {
      readonly allowed: false;
      readonly status: 401 | 403;
      readonly code: string;
      readonly message: string;
    };

/** Paths that answer without a token. They must reveal nothing about projects or work. */
export const PUBLIC_PATHS: ReadonlySet<string> = new Set(['/api/health']);

/**
 * Admission rules for every HTTP request and WebSocket upgrade, in order:
 * 1. The Host header must name this loopback server, which defeats DNS rebinding.
 * 2. Browser requests are refused: any Origin header, or a cross-site fetch. Browsers cannot
 *    open a WebSocket without sending Origin, so no web page can drive the control plane.
 * 3. Everything except the public paths needs the bearer token.
 */
export function checkRequest(facts: RequestFacts, token: string): GuardDecision {
  const allowedHosts = [
    `127.0.0.1:${facts.localPort}`,
    `localhost:${facts.localPort}`,
    `[::1]:${facts.localPort}`,
  ];
  if (facts.host === undefined || !allowedHosts.includes(facts.host.toLowerCase())) {
    return deny(403, 'host_not_allowed', 'The Host header must name this loopback server.');
  }
  if (facts.origin !== undefined) {
    return deny(403, 'origin_not_allowed', 'Requests from browser origins are not accepted.');
  }
  if (facts.secFetchSite === 'cross-site') {
    return deny(403, 'cross_site_request', 'Cross-site requests are not accepted.');
  }
  if (PUBLIC_PATHS.has(facts.path)) return { allowed: true };
  const header = facts.authorization;
  if (
    header === undefined ||
    !header.startsWith('Bearer ') ||
    !sameSecret(header.slice(7), token)
  ) {
    return deny(401, 'unauthorized', 'A valid bearer token is required.');
  }
  return { allowed: true };
}

function deny(status: 401 | 403, code: string, message: string): GuardDecision {
  return { allowed: false, status, code, message };
}

/** Constant-time comparison; hashing first makes the inputs equal length. */
function sameSecret(candidate: string, secret: string): boolean {
  const a = createHash('sha256').update(candidate).digest();
  const b = createHash('sha256').update(secret).digest();
  return timingSafeEqual(a, b);
}

/** The header a loopback client sends to ask the control plane to prove it holds the token. */
export const PROOF_CHALLENGE_HEADER = 'x-halcyonic-challenge';
/** The header the loopback listener answers a challenge with. */
export const PROOF_HEADER = 'x-halcyonic-proof';

const PROOF_LABEL = 'halcyonic loopback proof v2\n';
const CHALLENGE = /^[0-9a-f]{64}$/;

/** Whether a challenge is 32 bytes as lowercase hex, the only kind answered. */
export function isProofChallenge(value: unknown): value is string {
  return typeof value === 'string' && CHALLENGE.test(value);
}

/**
 * The loopback address and port a connection reached, as the proof names it: `127.0.0.1:47800` or
 * `[::1]:47800`. An IPv4 address mapped into IPv6 is named as IPv4.
 */
export function proofAddress(host: string, port: number): string {
  const bare = host.replace(/^\[|\]$/g, '').replace(/^::ffff:(?=\d+\.)/, '');
  return bare.includes(':') ? `[${bare}]:${port}` : `${bare}:${port}`;
}

/**
 * The proof that whoever answers holds the access token: an HMAC-SHA256 under the token of a
 * label, the address and port the connection reached, and the client's fresh challenge. It reveals
 * nothing about the token, the label keeps it from serving as anything else, and the address keeps
 * a relay on another address or port from passing the real control plane's answer on as its own.
 */
export function loopbackProof(token: string, address: string, challenge: string): string {
  return createHmac('sha256', token)
    .update(PROOF_LABEL)
    .update(`${address}\n`)
    .update(challenge)
    .digest('hex');
}

/**
 * Where a loopback client may dial the control plane, by address, never by a name another listener
 * could also answer to: `localhost` is tried as 127.0.0.1, then as [::1].
 */
export function loopbackBases(host: string, port: number): string[] {
  if (host === 'localhost') return [`http://127.0.0.1:${port}`, `http://[::1]:${port}`];
  return [host.includes(':') ? `http://[${host}]:${port}` : `http://${host}:${port}`];
}

/**
 * Whether the server at `base`, a literal loopback address and port, proves it holds `token`,
 * asked through the public health check with a fresh challenge, before a loopback client sends the
 * token anywhere. While the control plane is stopped, another local account may listen on its
 * port; it gets no token this way, and relaying the challenge to the real control plane on another
 * port yields a proof for that port, not this one.
 */
export async function serverProvesToken(
  fetchImpl: typeof fetch,
  base: string,
  token: string,
): Promise<'proved' | 'unproved' | 'unreachable'> {
  const url = new URL(base);
  const address = proofAddress(url.hostname, Number(url.port));
  const challenge = randomBytes(32).toString('hex');
  let response: Response;
  try {
    response = await fetchImpl(`${base}/api/health`, {
      headers: { [PROOF_CHALLENGE_HEADER]: challenge },
      redirect: 'error',
      signal: AbortSignal.timeout(3000),
    });
    await response.arrayBuffer();
  } catch {
    return 'unreachable';
  }
  const proof = response.headers.get(PROOF_HEADER);
  if (proof === null || !/^[0-9a-f]{64}$/.test(proof)) return 'unproved';
  const expected = Buffer.from(loopbackProof(token, address, challenge), 'hex');
  return timingSafeEqual(Buffer.from(proof, 'hex'), expected) ? 'proved' : 'unproved';
}

/**
 * The first of `bases` whose server proves it holds `token`; otherwise whether anything answered
 * without proving it, or nothing answered at all.
 */
export async function provenBase(
  fetchImpl: typeof fetch,
  bases: readonly string[],
  token: string,
): Promise<{ readonly base: string } | 'unproved' | 'unreachable'> {
  let answered = false;
  for (const base of bases) {
    const outcome = await serverProvesToken(fetchImpl, base, token);
    if (outcome === 'proved') return { base };
    if (outcome === 'unproved') answered = true;
  }
  return answered ? 'unproved' : 'unreachable';
}

/** Why a loopback client did not send the token: what answered could not prove it, or nothing did. */
export class TokenNotSent extends Error {
  readonly reason: 'unproved' | 'unreachable';

  constructor(reason: 'unproved' | 'unreachable', base: string) {
    super(
      reason === 'unreachable'
        ? `The control plane is not answering at ${base}, so the access token was not sent.`
        : `Something answers at ${base}, but it can't prove it holds this Mac's access token, so the token was not sent. It may be another program, or another account on this Mac, listening while the control plane is stopped.`,
    );
    this.name = 'TokenNotSent';
    this.reason = reason;
  }
}

/**
 * A request that carries the token, sent only after the server at `base` proves again, just
 * before, that it holds it: a control plane that stopped since an earlier proof, and whatever took
 * its port, gets no token. It follows no redirect. Throws `TokenNotSent` instead of sending it.
 */
export async function fetchWithProof(
  fetchImpl: typeof fetch,
  base: string,
  token: string,
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const proof = await serverProvesToken(fetchImpl, base, token);
  if (proof !== 'proved') throw new TokenNotSent(proof, base);
  const headers = new Headers(init.headers);
  headers.set('authorization', `Bearer ${token}`);
  // fetch keeps the authorization header on a same-origin redirect, which no proof would precede.
  return fetchImpl(`${base}${path}`, { ...init, headers, redirect: 'error' });
}
