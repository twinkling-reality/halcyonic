import { createHmac, randomBytes, timingSafeEqual } from 'node:crypto';
import { constants } from 'node:fs';
import { lstat, open } from 'node:fs/promises';
import { request } from 'node:http';
import { homedir } from 'node:os';
import { join } from 'node:path';
import type { CatalogResponse } from '@halcyonic/contracts';
import { readDiscoveryAnswer, readDiscoveryFileDocument } from './wire.ts';

/** Every answer but a catalog: why there is none. */
export type CatalogFailure = Exclude<CatalogResponse, { availability: 'available' }>;

export function fail<A extends CatalogFailure['availability']>(
  availability: A,
  code: string,
  message: string,
): Extract<CatalogFailure, { availability: A }> {
  return { availability, reason: { code, message } } as Extract<
    CatalogFailure,
    { availability: A }
  >;
}

export function isFailure(value: object): value is CatalogFailure {
  return (
    'availability' in value && (value as { availability: unknown }).availability !== 'available'
  );
}

/** Where Phat Porcupine publishes `consumer.json`, as it resolves it itself. */
export function defaultPhatPorcupineHome(
  env: NodeJS.ProcessEnv = process.env,
  userHome: string = homedir(),
): string {
  return env.PHAT_PORCUPINE_HOME ?? join(userHome, '.phat-porcupine');
}

export const DISCOVERY_FILE = 'consumer.json';

/** The most a discovery document may hold, as a file or an answer: a real one is a few hundred bytes. */
export const DISCOVERY_MAX_BYTES = 16 * 1024;

/** The most a catalog answer may hold: the contract's own bound (`CATALOG_BOUNDS.bytes`). */
export const CATALOG_MAX_BYTES = 8 * 1024 * 1024;

export const DEFAULT_TIMEOUT_MS = 10_000;

/** The one address a version 1 instance may name: loopback, its port, this path, nothing else. */
const BASE_URL = /^http:\/\/127\.0\.0\.1:([1-9][0-9]{0,4})\/consumer\/v1$/;

/** What the discovery file says, once it is known to be safe to read and in version 1's shape. */
export interface DiscoveryFile {
  readonly instanceId: string;
  readonly secret: Buffer;
  readonly port: number;
  readonly sample: boolean;
  readonly testGitHub: boolean;
}

/** An instance that answered a fresh challenge with the file's secret: safe to send the credential to. */
export interface Instance {
  readonly instanceId: string;
  readonly port: number;
  readonly sample: boolean;
  readonly testGitHub: boolean;
}

/**
 * Reads the discovery file only when it is a regular file, not a link, owned by this process's user
 * and writable by nobody else (AUDIT K2): any other account could otherwise point Halcyonic, and its
 * credential, at a port of its own. The file is opened without following a link and checked again
 * through the open handle, so it cannot be swapped between the checks and the read.
 */
export async function readDiscoveryFile(home: string): Promise<DiscoveryFile | CatalogFailure> {
  const path = join(home, DISCOVERY_FILE);
  const unsafe = () =>
    fail(
      'unavailable',
      'discovery_unsafe',
      "Phat Porcupine's discovery file is not one only you can change.",
    );
  let text: string;
  try {
    const named = await lstat(path);
    if (!named.isFile()) return unsafe();
    const file = await open(path, constants.O_RDONLY | constants.O_NOFOLLOW | constants.O_NONBLOCK);
    try {
      const opened = await file.stat();
      const uid = process.getuid?.();
      if (
        !opened.isFile() ||
        opened.ino !== named.ino ||
        opened.dev !== named.dev ||
        (uid !== undefined && opened.uid !== uid) ||
        (opened.mode & 0o022) !== 0
      )
        return unsafe();
      const buffer = Buffer.alloc(DISCOVERY_MAX_BYTES + 1);
      const { bytesRead } = await file.read(buffer, 0, buffer.length, 0);
      if (bytesRead > DISCOVERY_MAX_BYTES)
        return fail(
          'incompatible',
          'invalid_discovery',
          "Phat Porcupine's discovery file is too large.",
        );
      text = buffer.subarray(0, bytesRead).toString('utf8');
    } finally {
      await file.close();
    }
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code;
    if (code === 'ENOENT')
      return fail(
        'unavailable',
        'not_running',
        'Phat Porcupine is not running: it has not published its discovery file.',
      );
    if (code === 'ELOOP') return unsafe();
    return fail(
      'unavailable',
      'discovery_unreadable',
      "Phat Porcupine's discovery file cannot be read.",
    );
  }
  const document = readDiscoveryFileDocument(parseJson(text));
  if (document === null)
    return fail(
      'incompatible',
      'invalid_discovery',
      "Phat Porcupine's discovery file is not version 1's.",
    );
  const entry = document.contracts.find(
    (contract) => contract.name === 'phat-porcupine.catalog' && contract.major === 1,
  );
  if (entry === undefined)
    return fail(
      'incompatible',
      'unsupported_contract',
      'This Phat Porcupine serves no catalog contract of version 1.',
    );
  const match = BASE_URL.exec(entry.baseUrl);
  const port = match ? Number(match[1]) : 0;
  if (port < 1 || port > 65_535)
    return fail(
      'incompatible',
      'base_url_not_allowed',
      'Phat Porcupine names an address other than its loopback one.',
    );
  const secret = decodeBytes(document.secret, 32);
  if (secret === null)
    return fail(
      'incompatible',
      'invalid_discovery',
      "Phat Porcupine's discovery file holds no secret of 32 bytes.",
    );
  return {
    instanceId: document.instanceId,
    secret,
    port,
    sample: document.sample,
    testGitHub: document.testGitHub,
  };
}

/**
 * Finds the running instance and has it prove it holds the discovery file's secret (AUDIT K3): a
 * fresh challenge each time, whose HMAC-SHA256 under the secret's bytes must come back, compared in
 * constant time, beside the file's instance id. Nothing else on the port, even an account that read an
 * earlier discovery answer, can produce it. No credential is sent here.
 */
export async function connect(
  home: string,
  options: { readonly timeoutMs?: number; readonly signal?: AbortSignal } = {},
): Promise<Instance | CatalogFailure> {
  const file = await readDiscoveryFile(home);
  if (isFailure(file)) return file;
  const challenge = randomBytes(32);
  const reply = await get(
    file.port,
    `/consumer/v1/discovery?challenge=${challenge.toString('base64url')}`,
    null,
    options.timeoutMs ?? DEFAULT_TIMEOUT_MS,
    DISCOVERY_MAX_BYTES,
    options.signal,
  );
  if (!isReply(reply)) return unanswered(reply);
  if (reply.status === 400)
    return fail(
      'incompatible',
      'bad_request',
      'Phat Porcupine refused the challenge Halcyonic sent.',
    );
  if (reply.status !== 200) return refusal(reply.status, reply.body);
  const answer = readDiscoveryAnswer(reply.body);
  const mismatch = () =>
    fail(
      'unavailable',
      'instance_mismatch',
      "What answered isn't the Phat Porcupine that published the discovery file.",
    );
  if (answer === null)
    return fail(
      'incompatible',
      'invalid_discovery',
      "Phat Porcupine's discovery answer is not version 1's.",
    );
  if (answer.instanceId !== file.instanceId) return mismatch();
  const expected = createHmac('sha256', file.secret).update(challenge).digest();
  const proof = decodeBytes(answer.proof, expected.length);
  if (proof === null || !timingSafeEqual(proof, expected)) return mismatch();
  return {
    instanceId: file.instanceId,
    port: file.port,
    sample: file.sample,
    testGitHub: file.testGitHub,
  };
}

export interface Reply {
  readonly status: number;
  readonly headers: Readonly<Record<string, string | string[] | undefined>>;
  /** The parsed JSON body, or undefined when the body is empty or not JSON. */
  readonly body: unknown;
}

export const TIMED_OUT = 'timed-out';
export const TOO_LARGE = 'too-large';

export function isReply(value: Reply | typeof TIMED_OUT | typeof TOO_LARGE | null): value is Reply {
  return value !== null && typeof value === 'object';
}

/** Why a request got no answer to read. */
export function unanswered(value: typeof TIMED_OUT | typeof TOO_LARGE | null): CatalogFailure {
  if (value === TIMED_OUT)
    return fail('unavailable', 'timed_out', "Phat Porcupine didn't answer in time.");
  if (value === TOO_LARGE)
    return fail(
      'incompatible',
      'answer_too_large',
      "Phat Porcupine's answer is larger than version 1 allows.",
    );
  return fail(
    'unavailable',
    'unreachable',
    "Phat Porcupine's discovery file names a port nothing answers on.",
  );
}

/**
 * One GET to the instance's own port on 127.0.0.1, over `node:http`, which uses no proxy from the
 * environment and follows no redirect (AUDIT K4): the credential reaches only the address the
 * discovery file named and the proof confirmed. Resolves to null when nothing answers, to
 * `TIMED_OUT` after `timeoutMs`, and to `TOO_LARGE` once the body passes `maxBytes`, of which no
 * more is read. Rejects only when the caller's own signal aborts.
 */
export function get(
  port: number,
  path: string,
  credential: string | null,
  timeoutMs: number,
  maxBytes: number,
  signal?: AbortSignal,
  extraHeaders: Readonly<Record<string, string>> = {},
): Promise<Reply | typeof TIMED_OUT | typeof TOO_LARGE | null> {
  return new Promise((resolve, reject) => {
    let settled = false;
    const done = (value: Reply | typeof TIMED_OUT | typeof TOO_LARGE | null) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      signal?.removeEventListener('abort', aborted);
      resolve(value);
    };
    const outgoing = request(
      {
        host: '127.0.0.1',
        port,
        path,
        method: 'GET',
        agent: false,
        headers: {
          accept: 'application/json',
          ...extraHeaders,
          ...(credential === null ? {} : { authorization: `Bearer ${credential}` }),
        },
      },
      (response) => {
        const chunks: Buffer[] = [];
        let size = 0;
        response.on('data', (chunk: Buffer) => {
          size += chunk.byteLength;
          if (size > maxBytes) {
            response.destroy();
            outgoing.destroy();
            done(TOO_LARGE);
            return;
          }
          chunks.push(chunk);
        });
        response.on('end', () =>
          done({
            status: response.statusCode ?? 0,
            headers: response.headers,
            body: parseJson(Buffer.concat(chunks).toString('utf8')),
          }),
        );
        response.on('error', () => done(null));
      },
    );
    const timer = setTimeout(() => {
      outgoing.destroy();
      done(TIMED_OUT);
    }, timeoutMs);
    const aborted = () => {
      outgoing.destroy();
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        reject(signal?.reason);
      }
    };
    if (signal?.aborted) {
      aborted();
      return;
    }
    signal?.addEventListener('abort', aborted, { once: true });
    outgoing.on('error', () => done(null));
    outgoing.end();
  });
}

/**
 * Why Phat Porcupine refused, by its status and code, in Halcyonic's own words: its message is the
 * server's text and never passed on.
 */
export function refusal(status: number, body: unknown): CatalogFailure {
  const code =
    body !== null && typeof body === 'object' && 'error' in body
      ? String((body as { error?: { code?: unknown } }).error?.code ?? '')
      : '';
  if (status === 401)
    return fail(
      'unauthorized',
      'unauthorized',
      'Phat Porcupine refused the credential set up for Halcyonic.',
    );
  if (status === 503 && code === 'not_ready')
    return fail('unavailable', 'not_ready', 'Phat Porcupine has not gathered your projects yet.');
  if (status === 429)
    return fail(
      'unavailable',
      'rate_limited',
      'Phat Porcupine asked for a pause after too many refused credentials.',
    );
  if (status === 421)
    return fail(
      'unavailable',
      'host_not_allowed',
      'Phat Porcupine turned away the address Halcyonic used.',
    );
  if (status === 403)
    return fail(
      'unavailable',
      'origin_not_allowed',
      "Phat Porcupine took Halcyonic's request for a web page's.",
    );
  if (status === 500)
    return fail('unavailable', 'internal', 'Phat Porcupine failed while answering.');
  if (status === 404 || status === 405)
    return fail(
      'incompatible',
      status === 404 ? 'not_found' : 'method_not_allowed',
      'This Phat Porcupine does not serve the catalog Halcyonic reads.',
    );
  return fail(
    'unavailable',
    'unexpected_status',
    `Phat Porcupine answered with status ${status}, which version 1 does not use.`,
  );
}

/** A consumer credential in its one form, `ppc_` and 32 bytes in base64url; anything else is never sent. */
export function checkCredential(credential: string | null): string | CatalogFailure {
  if (credential === null || credential === '')
    return fail(
      'unauthorized',
      'credential_missing',
      'No Phat Porcupine credential is set up for Halcyonic.',
    );
  if (!/^ppc_[A-Za-z0-9_-]{43}$/.test(credential) || decodeBytes(credential.slice(4), 32) === null)
    return fail(
      'unauthorized',
      'credential_malformed',
      "The Phat Porcupine credential set up for Halcyonic isn't the right kind, so it wasn't sent.",
    );
  return credential;
}

/** Base64url of exactly `length` bytes, in its one canonical spelling, or null. */
function decodeBytes(text: string, length: number): Buffer | null {
  const bytes = Buffer.from(text, 'base64url');
  return bytes.length === length && bytes.toString('base64url') === text ? bytes : null;
}

export function parseJson(text: string): unknown {
  if (text === '') return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}
