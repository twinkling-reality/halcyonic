import type {
  EvaluationFailure,
  EvaluationResult,
  UsageLimit,
  UsageLimitsResponse,
  ValidationIssue,
} from '@halcyonic/contracts';
import { RequestBudget, WINDOW_MS } from './budget.ts';
import { toEvaluation } from './mapping.ts';
import {
  API_BASE_PATH,
  CREDENTIAL_PATTERN,
  DEFAULT_PORT,
  NATIVE_SESSION_ID_PATTERN,
  readLens,
  type SeorakAgent,
  validateOutcome,
  validateSession,
  validateUsageLimits,
  type WireUsageLimits,
} from './wire.ts';

/**
 * Halcyonic runtime kinds whose sessions Seorak observes, with the agent id Seorak resolves for
 * each. A runtime kind is Halcyonic's adapter type and an agent id is Seorak's vocabulary, so the
 * mapping is written out rather than assumed: the Claude Agent adapter (kind `claude-agent`) runs
 * Claude Code sessions, which Seorak names `claude-code`, and a Codex session's native id is its
 * thread id. Every other kind, such as `mock` or `opencode`, is not observed by Seorak.
 */
const AGENT_BY_RUNTIME_KIND: ReadonlyMap<string, SeorakAgent> = new Map([
  ['claude-agent', 'claude-code'],
  ['codex', 'codex'],
]);

export function seorakAgentFor(runtimeKind: string): SeorakAgent | null {
  return AGENT_BY_RUNTIME_KIND.get(runtimeKind) ?? null;
}

export interface SeorakOptions {
  /**
   * The port of Seorak's local plane, 4317 unless its owner configured another. The credential is
   * only ever sent to 127.0.0.1 on this port, the origin its audience names.
   */
  readonly port?: number;
  /** How long one request may take, in milliseconds. Defaults to 5000. */
  readonly timeoutMs?: number;
  /** The credential's request budget per minute. Defaults to 60, the budget Seorak gives. */
  readonly requestsPerMinute?: number;
}

export interface EvaluateOptions {
  /** The integration credential the owner issued for Halcyonic, or null when none is configured. */
  readonly credential: string | null;
  readonly signal?: AbortSignal;
}

/** An evaluation makes at most three requests: the resolve, the outcome and the verification lens. */
const REQUESTS_PER_EVALUATION = 3;

type Availability = EvaluationFailure['availability'];
type Failure<A extends Availability = Availability> = Extract<
  EvaluationFailure,
  { availability: A }
>;

function fail<A extends Availability>(availability: A, code: string, message: string): Failure<A> {
  return { availability, reason: { code, message } } as Failure<A>;
}

function isFailure(value: object): value is EvaluationFailure {
  return 'availability' in value;
}

function invalid(what: string, issues: readonly ValidationIssue[]): Failure<'incompatible'> {
  const issue = issues[0];
  const detail = issue === undefined ? '' : `: ${issue.path} ${issue.message}`;
  return fail(
    'incompatible',
    'invalid_document',
    `The ${what} does not match Seorak's integration API v1${detail}.`,
  );
}

/** A document that is valid on its own but contradicts the request or itself. */
function incoherent(what: string): Failure<'incompatible'> {
  return fail('incompatible', 'invalid_document', `Seorak's ${what}.`);
}

/** The scope each read needs, the same over HTTP as over MCP (ADR 007, section 2). */
const SESSIONS = 'sessions:read';
const REPLAY = 'replay:read';
const LIMITS = 'limits:read';

/** How a person reads the agents Halcyonic knows; any other agent is shown by its own id. */
const AGENT_LABELS: ReadonlyMap<string, string> = new Map([
  ['claude-code', 'Claude Code'],
  ['codex', 'Codex'],
]);
const AGENT_ID = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

/** Why Seorak has no reading, by its reason. */
const NO_READING: ReadonlyMap<string, Failure<'unavailable' | 'unauthorized'>> = new Map<
  string,
  Failure<'unavailable' | 'unauthorized'>
>([
  [
    'not-captured',
    fail('unavailable', 'not_captured', 'Seorak has not captured a provider limit.'),
  ],
  [
    'outside-credential-restriction',
    fail(
      'unauthorized',
      'outside_credential_restriction',
      'The Seorak credential is restricted to a project or dates, and Seorak serves provider limits only to an unrestricted credential.',
    ),
  ],
]);

function toUsageLimits(document: WireUsageLimits, now: number): UsageLimitsResponse {
  if (document.availability.state === 'unavailable') {
    if (document.readings.length > 0)
      return incoherent('usage limits answer carries readings although it says it is unavailable');
    return (
      NO_READING.get(document.availability.reason ?? '') ??
      fail(
        'unavailable',
        'unknown_reason',
        'Seorak has no provider limit, for a reason this client cannot name.',
      )
    );
  }
  const readings: UsageLimit[] = [];
  for (const row of document.readings) {
    if (
      !AGENT_ID.test(row.agent) ||
      (row.window !== 'rolling-5h' && row.window !== 'weekly') ||
      (row.freshness !== 'fresh' && row.freshness !== 'stale') ||
      row.source !== 'provider-reported'
    )
      continue;
    // A reading past its reset says nothing about the new window (Seorak: read again instead).
    if (Date.parse(row.resetsAt) <= now) continue;
    readings.push({
      agent: row.agent,
      label: AGENT_LABELS.get(row.agent) ?? row.agent,
      window: row.window,
      used_percent: row.usedPercent,
      resets_at: new Date(row.resetsAt).toISOString(),
      observed_at: new Date(row.observedAt).toISOString(),
      freshness: row.freshness,
      // Halcyonic has no account identity boundary, so no reading is ever attributed to one.
      account: { state: 'unidentified' },
    });
  }
  const [first, ...rest] = readings;
  if (first === undefined)
    return document.readings.length === 0
      ? fail('unavailable', 'not_captured', 'Seorak has not captured a provider limit.')
      : fail(
          'unavailable',
          'no_current_reading',
          'Every provider limit Seorak holds has reset since it was observed.',
        );
  return {
    availability: 'available',
    source: { system: 'seorak', synthetic: false, api_version: 'v1' },
    readings: [first, ...rest],
  };
}

type Miss = readonly [Availability, string, string];

/**
 * What a resolve that found no session means, by Seorak's reason. A miss is always `not-captured`
 * or `outside-credential-restriction` (ADR 007, section 2); the other reasons Seorak publishes are
 * read by what they say.
 */
const MISS: ReadonlyMap<string, Miss> = new Map<string, Miss>([
  [
    'not-captured',
    [
      'not_found',
      'not_captured',
      'Seorak has not captured this session. A session launched moments ago may not have reached a hook yet, so retry with backoff.',
    ],
  ],
  ['not-retained', ['not_found', 'not_retained', 'Seorak no longer retains this session.']],
  [
    'not-yet-computed',
    [
      'not_found',
      'not_yet_computed',
      'Seorak has not computed this session yet; retry with backoff.',
    ],
  ],
  [
    'outside-credential-restriction',
    [
      'unauthorized',
      'outside_credential_restriction',
      "The Seorak credential's project or date restriction excludes this session.",
    ],
  ],
  [
    'temporarily-unavailable',
    [
      'unavailable',
      'temporarily_unavailable',
      'Seorak cannot resolve the session for now; retry with backoff.',
    ],
  ],
  [
    'result-limit',
    [
      'unavailable',
      'result_limit',
      'Seorak did not resolve the session because a result limit was reached.',
    ],
  ],
]);

/** A reason v1 added after this client: unavailable for a reason it cannot name (ADR 007). */
const UNNAMED_MISS: Miss = [
  'unavailable',
  'unknown_reason',
  'Seorak did not resolve the session, for a reason this client cannot name.',
];

interface Reply {
  readonly status: number;
  readonly retryAfter: string | null;
  /** The parsed JSON body, or undefined when the body is not JSON. */
  readonly body: unknown;
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/** The longest pause a Retry-After imposes, so that a wrong header cannot silence Seorak for good. */
const MAX_PAUSE_MS = 3_600_000;

/** Retry-After is delay seconds or an HTTP date (RFC 9110). Without a usable one, wait a minute. */
function retryTime(header: string | null, now: number): number {
  const value = header?.trim() ?? '';
  if (/^\d+$/.test(value)) return now + Math.min(Number(value) * 1000, MAX_PAUSE_MS);
  const date = Date.parse(value);
  if (!Number.isFinite(date)) return now + WINDOW_MS;
  return Math.min(Math.max(now, date), now + MAX_PAUSE_MS);
}

/**
 * Reads what Seorak measured about the session behind one execution, through the read-only
 * integration API v1 of Seorak's local plane: it resolves the runtime's own session id to Seorak's
 * session, then reads the session's outcome and verification lens.
 *
 * One instance keeps the credential's request budget, so a process shares one client. The
 * credential is passed on every call instead, because the owner may issue, replace or revoke it
 * at any time.
 */
export class SeorakClient {
  readonly #origin: string;
  readonly #timeoutMs: number;
  readonly #budget: RequestBudget;

  constructor(options: SeorakOptions = {}) {
    const port = options.port ?? DEFAULT_PORT;
    const limit = options.requestsPerMinute ?? 60;
    if (!Number.isInteger(port) || port < 1 || port > 65_535)
      throw new RangeError(`${port} is not a TCP port.`);
    if (!Number.isInteger(limit) || limit < REQUESTS_PER_EVALUATION)
      throw new RangeError(`An evaluation takes ${REQUESTS_PER_EVALUATION} requests a minute.`);
    this.#origin = `http://127.0.0.1:${port}`;
    this.#timeoutMs = options.timeoutMs ?? 5_000;
    this.#budget = new RequestBudget(limit);
  }

  /**
   * The provider-reported limits Seorak last observed, account wide. One request, made only when a
   * person asks, under the `limits:read` scope. Readings Halcyonic cannot phrase honestly (another
   * window, another source, an agent id outside the pattern) and readings past their reset are
   * dropped; when none is left, the answer is unavailable, never 0%.
   */
  async usageLimits(options: EvaluateOptions): Promise<UsageLimitsResponse> {
    const credential = this.#check(options.credential);
    if (typeof credential !== 'string') return credential;
    const retryAt = this.#budget.reserve(1, Date.now());
    if (retryAt !== null)
      return fail(
        'unavailable',
        'rate_limited',
        `Seorak's request budget allows no request until ${new Date(retryAt).toISOString()}, so none was sent.`,
      );
    this.#budget.start(Date.now());
    const reply = await this.#read(
      'usage limits',
      `${API_BASE_PATH}/usage-limits`,
      LIMITS,
      credential,
      undefined,
      options.signal,
      // A Seorak without the read does not know the route: a setup problem, not a broken plane.
      fail(
        'unavailable',
        'limits_not_served',
        `Seorak at ${this.#origin} does not serve provider usage limits (HTTP 404); update and restart it.`,
      ),
    );
    if (isFailure(reply)) {
      if (reply.availability === 'not_found')
        return { availability: 'unavailable', reason: reply.reason };
      // Seorak answers 403 insufficient_scope for a credential without limits:read.
      if (reply.reason.code === 'credential_forbidden')
        return fail('unauthorized', 'insufficient_scope', reply.reason.message);
      return reply;
    }
    const parsed = validateUsageLimits(reply.value);
    if (!parsed.ok) return invalid('usage limits answer', parsed.issues);
    return toUsageLimits(parsed.value, Date.now());
  }

  /**
   * `runtimeKind` and `nativeId` are the execution's runtime kind and the runtime's own session
   * id, the key every orchestrator holds. A session launched moments ago may not be captured yet,
   * so `not_found` can change to `available` later; the caller retries with backoff.
   */
  async evaluate(
    runtimeKind: string,
    nativeId: string,
    options: EvaluateOptions,
  ): Promise<EvaluationResult> {
    const agent = seorakAgentFor(runtimeKind);
    if (agent === null)
      return fail(
        'unavailable',
        'runtime_not_observed',
        `Seorak does not observe sessions of the ${runtimeKind} runtime.`,
      );
    if (!NATIVE_SESSION_ID_PATTERN.test(nativeId))
      return fail(
        'not_found',
        'not_resolvable',
        "Seorak resolves only session ids of up to 256 letters, digits, '.', '_', ':' or '-' that start with a letter or digit.",
      );
    const credential = this.#check(options.credential);
    if (typeof credential !== 'string') return credential;
    const retryAt = this.#budget.reserve(REQUESTS_PER_EVALUATION, Date.now());
    if (retryAt !== null)
      return fail(
        'unavailable',
        'rate_limited',
        `Seorak's request budget allows no request until ${new Date(retryAt).toISOString()}, so none was sent.`,
      );

    let unused = REQUESTS_PER_EVALUATION;
    const read = (what: string, path: string, scope: string, body?: object) => {
      unused--;
      this.#budget.start(Date.now());
      return this.#read(what, path, scope, credential, body, options.signal);
    };
    try {
      const resolve = await read('session resolve', `${API_BASE_PATH}/sessions/resolve`, SESSIONS, {
        agent,
        nativeSessionId: nativeId,
      });
      if (isFailure(resolve)) return resolve;
      const resolved = validateSession(resolve.value);
      if (!resolved.ok) return invalid('session resolve answer', resolved.issues);
      const { session, availability } = resolved.value;
      // Only `unavailable` carries a null payload (ADR 007, section 2), and it says why.
      if (session === null) {
        if (availability.state !== 'unavailable' || availability.reason === null)
          return incoherent('resolve answer names no session but does not say why');
        const [state, code, message] = MISS.get(availability.reason) ?? UNNAMED_MISS;
        return fail(state, code, message);
      }
      // Both halves of the identity must match (ADR 007).
      if (session.agent !== agent)
        return incoherent('resolve answer names a session of another agent than the one requested');
      if (availability.state === 'unavailable')
        return incoherent('resolve answer names a session although it says it is unavailable');

      const ref = session.sessionRef;
      const path = `${API_BASE_PATH}/sessions/${encodeURIComponent(ref)}`;
      const outcomeReply = await read('session outcome', `${path}/outcome`, SESSIONS);
      if (isFailure(outcomeReply)) return outcomeReply;
      const outcome = validateOutcome(outcomeReply.value);
      if (!outcome.ok) return invalid('session outcome', outcome.issues);
      if (outcome.value.sessionRef !== ref)
        return incoherent('outcome names a different session than the one resolved');
      if (outcome.value.outcome === null && outcome.value.availability.state !== 'unavailable')
        return incoherent('outcome carries no measure although it says it is available');

      const lensReply = await read('verification lens', `${path}/replay/verification`, REPLAY);
      if (isFailure(lensReply)) return lensReply;
      const lens = readLens(lensReply.value);
      if (!lens.ok) return invalid('verification lens', lens.issues);
      const { result } = lens.value;
      if (result !== null && result.sessionRef !== ref)
        return incoherent('verification lens names a different session than the one resolved');
      if ((result === null) !== (lens.value.availability.state === 'unavailable'))
        return incoherent('verification lens has a result exactly when it says it is unavailable');
      return {
        availability: 'available',
        evaluation: toEvaluation(resolved.value, session.costUsd, outcome.value, lens.value),
      };
    } finally {
      this.#budget.release(unused);
    }
  }

  /** The credential to send, or why none will be sent. It is checked before any request carries it. */
  #check(credential: string | null): string | Failure<'unauthorized'> {
    const issue = `Issue one in Seorak's dashboard (${this.#origin}/dashboard) for the audience ${this.#origin}${API_BASE_PATH} with the ${SESSIONS}, ${REPLAY} and ${LIMITS} scopes.`;
    if (credential === null)
      return fail(
        'unauthorized',
        'credential_missing',
        `No Seorak integration credential is configured. ${issue}`,
      );
    if (!CREDENTIAL_PATTERN.test(credential))
      return fail(
        'unauthorized',
        'credential_malformed',
        'The configured Seorak credential is not an integration credential (srkx_), so it was not sent.',
      );
    return credential;
  }

  /**
   * One request, interpreted by its status as ADR 007 (section 2) states them: 401 for every
   * credential failure, 403 for a valid credential without the scope, 429 with `Retry-After` in
   * whole seconds. Error bodies are not part of the contract and are never read. Rejects only when
   * the caller's own signal aborts.
   */
  async #read(
    what: string,
    path: string,
    scope: string,
    credential: string,
    body: object | undefined,
    signal: AbortSignal | undefined,
    unknownRoute?: Failure,
  ): Promise<{ value: unknown } | Failure> {
    const reply = await this.#send(what, path, credential, body, signal);
    if (isFailure(reply)) return reply;
    if (reply.status === 200) return { value: reply.body };
    if (reply.status === 404 && unknownRoute !== undefined) return unknownRoute;
    if (reply.status === 401)
      return fail(
        'unauthorized',
        'credential_rejected',
        `Seorak refused the integration credential as unknown, expired, revoked or issued for another audience. Issue a new one in Seorak's dashboard (${this.#origin}/dashboard) for the audience ${this.#origin}${API_BASE_PATH}.`,
      );
    if (reply.status === 403)
      return fail(
        'unauthorized',
        'credential_forbidden',
        `Seorak refused the ${what} request (HTTP 403): the credential lacks the ${scope} scope it needs. Halcyonic needs one credential with ${SESSIONS}, ${REPLAY} and ${LIMITS}.`,
      );
    if (reply.status === 429) {
      const until = retryTime(reply.retryAfter, Date.now());
      this.#budget.pauseUntil(until);
      return fail(
        'unavailable',
        'rate_limited',
        `Seorak refused the ${what} request over a request budget, the credential's own or the one all credentials share, and asked for no request until ${new Date(until).toISOString()}.`,
      );
    }
    if (reply.status >= 500)
      return fail(
        'unavailable',
        'server_error',
        `Seorak failed to answer the ${what} request (HTTP ${reply.status}).`,
      );
    return fail(
      'incompatible',
      'unexpected_status',
      `Seorak answered the ${what} request with HTTP ${reply.status}, which this client does not read from integration API v1.`,
    );
  }

  async #send(
    what: string,
    path: string,
    credential: string,
    body: object | undefined,
    signal: AbortSignal | undefined,
  ): Promise<Reply | Failure<'unavailable'>> {
    const signals = [AbortSignal.timeout(this.#timeoutMs), ...(signal ? [signal] : [])];
    try {
      const response = await fetch(new URL(path, this.#origin), {
        method: body === undefined ? 'GET' : 'POST',
        headers: {
          accept: 'application/json',
          authorization: `Bearer ${credential}`,
          ...(body === undefined ? {} : { 'content-type': 'application/json' }),
        },
        // A native id travels only in a body, never in a URL (ADR 007).
        body: body === undefined ? null : JSON.stringify(body),
        // A credential must never follow a redirect to another origin.
        redirect: 'error',
        signal: AbortSignal.any(signals),
      });
      const text = await response.text();
      return {
        status: response.status,
        retryAfter: response.headers.get('retry-after'),
        body: parseJson(text),
      };
    } catch (error) {
      if (signal?.aborted) throw signal.reason;
      if (error instanceof Error && error.name === 'TimeoutError')
        return fail(
          'unavailable',
          'unreachable',
          `Seorak did not answer the ${what} request within ${this.#timeoutMs} ms.`,
        );
      if (error instanceof TypeError)
        return (error.cause as { code?: unknown } | undefined)?.code === 'ECONNREFUSED'
          ? fail(
              'unavailable',
              'not_running',
              `Nothing answers at ${this.#origin}: Seorak's local plane is not running.`,
            )
          : fail(
              'unavailable',
              'unreachable',
              `Seorak's local plane at ${this.#origin} could not be reached for the ${what} request.`,
            );
      throw error;
    }
  }
}
