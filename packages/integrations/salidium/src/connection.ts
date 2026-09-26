import { readFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import type { UnderstandingFailure, ValidationIssue } from '@halcyonic/contracts';
import {
  CONSUMER_BASE_PATH,
  CONSUMER_TOKEN_PATTERN,
  validateDiscovery,
  type WireDiscovery,
} from './wire.ts';

export interface SalidiumOptions {
  /** Salidium's state directory, where it publishes `consumer.json`. See `defaultSalidiumHome`. */
  readonly home: string;
  /** The consumer credential the person created for Halcyonic, or null when none is configured. */
  readonly credential: string | null;
  /** How long one request may take, in milliseconds. Defaults to 5000. */
  readonly timeoutMs?: number;
}

export const DEFAULT_TIMEOUT_MS = 5_000;

/** Where Salidium keeps its state, resolved as Salidium resolves it: `$SALIDIUM_HOME`, else `~/.salidium`. */
export function defaultSalidiumHome(
  env: NodeJS.ProcessEnv = process.env,
  userHome: string = homedir(),
): string {
  return env.SALIDIUM_HOME ?? join(userHome, '.salidium');
}

type Availability = UnderstandingFailure['availability'];
export type Failure<A extends Availability = Availability> = Extract<
  UnderstandingFailure,
  { availability: A }
>;

export function fail<A extends Availability>(
  availability: A,
  code: string,
  message: string,
): Failure<A> {
  return { availability, reason: { code, message } } as Failure<A>;
}

export function isFailure(value: object): value is UnderstandingFailure {
  return 'availability' in value;
}

export function invalid(what: string, issues: readonly ValidationIssue[]): Failure<'incompatible'> {
  const issue = issues[0];
  const detail = issue === undefined ? '' : `: ${issue.path} ${issue.message}`;
  return fail(
    'incompatible',
    'invalid_document',
    `The ${what} does not match Salidium consumer contract v1${detail}.`,
  );
}

/** A Salidium daemon that proved it wrote the discovery file this client read. */
export interface Instance {
  readonly origin: string;
  /** The discovery document the daemon itself served. */
  readonly discovery: WireDiscovery;
}

export interface Reply {
  readonly status: number;
  /** The parsed JSON body, or undefined when the body is not JSON. */
  readonly body: unknown;
}

/**
 * One GET. Resolves to null when Salidium cannot be reached or does not answer in time. Rejects
 * only when the caller's own signal aborts, because that is the caller's decision rather than a
 * state of Salidium.
 */
export async function get(
  url: URL,
  credential: string | null,
  timeoutMs: number,
  signal: AbortSignal | undefined,
): Promise<Reply | null> {
  const signals = [AbortSignal.timeout(timeoutMs), ...(signal ? [signal] : [])];
  try {
    const response = await fetch(url, {
      headers: {
        accept: 'application/json',
        ...(credential === null ? {} : { authorization: `Bearer ${credential}` }),
      },
      // The contract never redirects, and a credential must never follow a redirect elsewhere.
      redirect: 'error',
      signal: AbortSignal.any(signals),
    });
    const text = await response.text();
    return { status: response.status, body: parseJson(text) };
  } catch (error) {
    if (signal?.aborted) throw signal.reason;
    if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'TypeError'))
      return null;
    throw error;
  }
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/** The credential to send, or why none will be sent. It is checked before any request carries it. */
export function checkCredential(credential: string | null): string | Failure<'unauthorized'> {
  if (credential === null)
    return fail(
      'unauthorized',
      'credential_missing',
      'No Salidium consumer credential is configured. Create one with `salidium consumer create <label>`.',
    );
  if (!CONSUMER_TOKEN_PATTERN.test(credential))
    return fail(
      'unauthorized',
      'credential_malformed',
      'The configured Salidium credential is not a consumer token, so it was not sent.',
    );
  return credential;
}

/** A document that declares a consumer contract other than the one this client reads. */
function declaresOtherContract(value: unknown): boolean {
  const contract = (value as { contract?: unknown } | null)?.contract;
  if (typeof contract !== 'object' || contract === null) return false;
  const { name, major } = contract as { name?: unknown; major?: unknown };
  return name !== 'salidium.consumer' || major !== 1;
}

/**
 * Finds the running daemon exactly as the contract specifies: read `consumer.json` from Salidium's
 * home, fetch the discovery endpoint it names without a credential, and accept the daemon only if
 * both name the same `instanceId`. Otherwise the port may belong to another process, for example
 * after Salidium stopped without removing the file, and the credential must not go there.
 */
export async function connect(
  home: string,
  timeoutMs: number,
  signal: AbortSignal | undefined,
): Promise<Instance | Failure<'unavailable' | 'incompatible'>> {
  let text: string;
  try {
    text = await readFile(join(home, 'consumer.json'), 'utf8');
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT')
      return fail(
        'unavailable',
        'not_running',
        'Salidium is not running: it has not published its discovery file.',
      );
    return fail('unavailable', 'discovery_unreadable', "Salidium's discovery file cannot be read.");
  }
  const value = parseJson(text);
  if (declaresOtherContract(value))
    return fail(
      'incompatible',
      'unsupported_contract',
      'Salidium offers a consumer contract other than salidium.consumer major version 1, which this client reads.',
    );
  const file = validateDiscovery(value);
  if (!file.ok) return invalid('discovery file', file.issues);
  let origin: string;
  try {
    origin = new URL(file.value.baseUrl).origin;
  } catch {
    return invalid('discovery file', [{ path: '/baseUrl', message: 'is not a valid URL' }]);
  }

  const reply = await get(
    new URL(`${CONSUMER_BASE_PATH}/discovery`, origin),
    null,
    timeoutMs,
    signal,
  );
  if (reply === null)
    return fail(
      'unavailable',
      'unreachable',
      "The port in Salidium's discovery file did not answer; Salidium may have stopped without removing the file.",
    );
  const served = validateDiscovery(reply.body);
  if (reply.status !== 200 || !served.ok || served.value.instanceId !== file.value.instanceId)
    return fail(
      'unavailable',
      'instance_mismatch',
      "The port in Salidium's discovery file did not answer as the Salidium instance that wrote it, so no credential was sent.",
    );
  return { origin, discovery: served.value };
}
