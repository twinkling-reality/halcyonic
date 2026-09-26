import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
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
