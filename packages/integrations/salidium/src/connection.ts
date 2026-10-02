import { readFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import type { UnderstandingFailure, ValidationIssue } from '@halcyonic/contracts';
import {
  CONSUMER_BASE_PATH,
  CONSUMER_TOKEN_PATTERN,
  isMajorOneEntry,
  validateContractEntry,
  validateDiscovery,
  validateError,
  validateProviders,
  type WireContractEntry,
  type WireDiscovery,
} from './wire.ts';

export interface SalidiumOptions {
  /** Salidium's state directory, where it publishes `consumer.json`. See `defaultSalidiumHome`. */
  readonly home: string;
  /**
   * The consumer credential the person created for Halcyonic, or null when none is configured; or
   * a function that reads it, called only once a request will carry it, which may say why there
   * is none instead.
   */
  readonly credential: string | null | (() => string | UnderstandingFailure);
  /** How long one request may take, in milliseconds. Defaults to 10,000. */
  readonly timeoutMs?: number;
  /**
   * How long one whole read may take, its requests together, in milliseconds. Defaults to 12,000,
   * so a read always answers inside a headset's 15 seconds.
   */
  readonly budgetMs?: number;
}

/**
 * Generous, because a Salidium catching up, as after an upgrade replaying an older session on its
 * main thread for 5 to 8 seconds, answers late rather than not at all.
 */
export const DEFAULT_TIMEOUT_MS = 10_000;
export const DEFAULT_BUDGET_MS = 12_000;

/** What `get` gives back when Salidium did not answer in time, as distinct from not at all. */
export const TIMED_OUT = 'timed-out';

/** Salidium did not answer in time. Said without the product's name, which the provenance line gives. */
export function timedOut(): Failure<'unavailable'> {
  return fail(
    'unavailable',
    'timed_out',
    'No answer in time: it may still be catching up after an update. Press Refresh in a moment.',
  );
}

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

/** A discovery document and its entry for the major version this client implements. */
export interface Discovered {
  readonly discovery: WireDiscovery;
  readonly contract: WireContractEntry;
}

/** A Salidium daemon that proved it wrote the discovery file this client read. */
export interface Instance extends Discovered {
  readonly origin: string;
  /**
   * The providers this instance says it observes now, or null when it does not say: a daemon of
   * contract 1.0, or a document without the list.
   */
  readonly providers: ReadonlySet<string> | null;
}

/**
 * The providers a verified discovery document declares, read only beside a major 1 entry of minor 1
 * or later, as the contract adds them there; null when it declares none.
 */
export function declaredProviders(
  discovered: Discovered,
): ReadonlySet<string> | null | Failure<'incompatible'> {
  if (discovered.contract.minor < 1) return null;
  const listed = (discovered.discovery as { providers?: unknown }).providers;
  if (listed === undefined) return null;
  const providers = validateProviders(listed);
  if (!providers.ok)
    return invalid(
      'discovery document',
      providers.issues.map((issue) => ({
        path: `/providers${issue.path === '/' ? '' : issue.path}`,
        message: issue.message,
      })),
    );
  return new Set(providers.value.map((provider) => provider.id));
}

/**
 * Reads a discovery document the way the contract asks: take the entry for `salidium.consumer`
 * major 1 and ignore every other, so a later major listed beside it never hides it.
 */
export function readDiscovery(value: unknown, what: string): Discovered | Failure<'incompatible'> {
  const discovery = validateDiscovery(value);
  if (!discovery.ok) return invalid(what, discovery.issues);
  const index = discovery.value.contracts.findIndex(isMajorOneEntry);
  if (index === -1)
    return fail(
      'incompatible',
      'unsupported_contract',
      'Salidium does not offer salidium.consumer major version 1, the contract this client reads.',
    );
  const contract = validateContractEntry(discovery.value.contracts[index]);
  if (!contract.ok)
    return invalid(
      what,
      contract.issues.map((issue) => ({
        path: `/contracts/${index}${issue.path === '/' ? '' : issue.path}`,
        message: issue.message,
      })),
    );
  return { discovery: discovery.value, contract: contract.value };
}

export interface Reply {
  readonly status: number;
  /** The parsed JSON body, or undefined when the body is not JSON. */
  readonly body: unknown;
}

/**
 * One GET. Resolves to null when Salidium cannot be reached, and to `TIMED_OUT` when it does not
 * answer within `timeoutMs` or before `deadline`, the whole read's budget. Rejects only when the
 * caller's own signal aborts, because that is the caller's decision rather than a state of
 * Salidium.
 */
export async function get(
  url: URL,
  credential: string | null,
  timeoutMs: number,
  signal: AbortSignal | undefined,
  deadline?: AbortSignal,
): Promise<Reply | typeof TIMED_OUT | null> {
  const signals = [
    AbortSignal.timeout(timeoutMs),
    ...(deadline ? [deadline] : []),
    ...(signal ? [signal] : []),
  ];
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
    if (error instanceof Error && error.name === 'TimeoutError') return TIMED_OUT;
    if (error instanceof Error && error.name === 'TypeError') return null;
    throw error;
  }
}

export function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/**
 * The availability behind a refusal the contract defines for every path: the loopback guard's
 * `host-not-allowed` and `origin-not-allowed`, and a failure inside Salidium. Null for any other
 * answer, which the caller interprets for its own request.
 */
export function refusal(
  status: number,
  body: unknown,
  what: string,
): Failure<'unavailable'> | null {
  const error = validateError(body);
  const code = error.ok ? error.value.error : undefined;
  if (status === 421 && code === 'host-not-allowed')
    return fail(
      'unavailable',
      'host_not_allowed',
      `Salidium refused the ${what} request: it accepts only its own loopback address and port.`,
    );
  if (status === 403 && code === 'origin-not-allowed')
    return fail(
      'unavailable',
      'origin_not_allowed',
      `Salidium refused the ${what} request as coming from a browser origin or another site.`,
    );
  if (status >= 500)
    return fail(
      'unavailable',
      'server_error',
      `Salidium failed to answer the ${what} request (HTTP ${status}).`,
    );
  return null;
}

/** Salidium answered 401 to a request that carried the credential. */
export function credentialRejected(): Failure<'unauthorized'> {
  return fail(
    'unauthorized',
    'credential_rejected',
    'Salidium refused the consumer credential; it may have been revoked. Create a new one with `salidium consumer create <label>`.',
  );
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

/**
 * Finds the running daemon exactly as the contract specifies: read `consumer.json` from Salidium's
 * home, fetch the discovery endpoint its major 1 entry names without a credential, and accept the
 * daemon only if both name the same `instanceId`. Otherwise the port may belong to another
 * process, for example after Salidium stopped without removing the file, and the credential must
 * not go there.
 */
export async function connect(
  home: string,
  timeoutMs: number,
  signal: AbortSignal | undefined,
  deadline?: AbortSignal,
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
  const file = readDiscovery(parseJson(text), 'discovery file');
  if (isFailure(file)) return file;
  let origin: string;
  try {
    origin = new URL(file.contract.baseUrl).origin;
  } catch {
    return invalid('discovery file', [{ path: '/contracts', message: 'names an invalid URL' }]);
  }

  const url = new URL(`${CONSUMER_BASE_PATH}/discovery`, origin);
  const reply = await get(url, null, timeoutMs, signal, deadline);
  if (reply === TIMED_OUT) return timedOut();
  if (reply === null)
    return fail(
      'unavailable',
      'unreachable',
      "The port in Salidium's discovery file did not answer; Salidium may have stopped without removing the file.",
    );
  const mismatch = fail(
    'unavailable',
    'instance_mismatch',
    "The port in Salidium's discovery file did not answer as the Salidium instance that wrote it, so no credential was sent.",
  );
  if (reply.status !== 200) return refusal(reply.status, reply.body, 'discovery') ?? mismatch;
  const served = readDiscovery(reply.body, 'discovery document');
  if (isFailure(served) || served.discovery.instanceId !== file.discovery.instanceId)
    return mismatch;
  const providers = declaredProviders(served);
  if (providers !== null && isFailure(providers)) return providers;
  return { origin, ...served, providers };
}
