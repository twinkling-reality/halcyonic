import type { UnderstandingResult } from '@halcyonic/contracts';
import {
  checkCredential,
  connect,
  DEFAULT_TIMEOUT_MS,
  type Failure,
  fail,
  get,
  invalid,
  isFailure,
  type Reply,
  type SalidiumOptions,
} from './connection.ts';
import { toUnderstanding } from './report.ts';
import {
  CONSUMER_BASE_PATH,
  NATIVE_SESSION_ID_PATTERN,
  validateError,
  validateLookup,
  validateReport,
} from './wire.ts';

/** The provider ids Salidium's consumer contract uses for the runtimes it observes. */
export type SalidiumProvider = 'claude-code' | 'codex';

/**
 * Halcyonic runtime kinds whose sessions Salidium observes, with the provider id Salidium uses for
 * each. A runtime kind is Halcyonic's adapter type and a provider id is Salidium's vocabulary, so
 * the mapping is written out rather than assumed. The Claude Code and Codex adapters do not exist
 * yet; they must use these kinds, or this table changes with them. Every other kind, such as
 * `mock` or `opencode`, is not observed by Salidium.
 */
const PROVIDER_BY_RUNTIME_KIND: ReadonlyMap<string, SalidiumProvider> = new Map([
  ['claude-code', 'claude-code'],
  ['codex', 'codex'],
]);

export function salidiumProviderFor(runtimeKind: string): SalidiumProvider | null {
  return PROVIDER_BY_RUNTIME_KIND.get(runtimeKind) ?? null;
}

export interface UnderstandOptions {
  /**
   * Salidium's own id for the session, when already known from a `session_changed` notification.
   * It skips the lookup; the report must still name the requested session.
   */
  readonly sessionId?: string;
  readonly signal?: AbortSignal;
}

/**
 * Reads what Salidium understood about the session behind one execution, through Salidium's
 * read-only consumer contract v1. Stateless: every call finds the daemon and checks its instance
 * again before a credential is sent, because the daemon may restart or stop at any time.
 */
export class SalidiumClient {
  readonly #home: string;
  readonly #credential: string | null;
  readonly #timeoutMs: number;

  constructor(options: SalidiumOptions) {
    this.#home = options.home;
    this.#credential = options.credential;
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }

  /**
   * `runtimeKind` and `nativeId` are the execution's runtime kind and the runtime's own session
   * id, the key every orchestrator holds. A session launched a moment ago may not have reported
   * to Salidium yet, so `not_found` can change to `available` later.
   */
  async understand(
    runtimeKind: string,
    nativeId: string,
    options: UnderstandOptions = {},
  ): Promise<UnderstandingResult> {
    const provider = salidiumProviderFor(runtimeKind);
    if (provider === null)
      return fail(
        'unavailable',
        'runtime_not_observed',
        `Salidium does not observe sessions of the ${runtimeKind} runtime.`,
      );
    if (!NATIVE_SESSION_ID_PATTERN.test(nativeId))
      return fail(
        'not_found',
        'not_observable',
        "Salidium's contract cannot name a session id with control characters or over 512 characters.",
      );
    const { signal } = options;
    const instance = await connect(this.#home, this.#timeoutMs, signal);
    if (isFailure(instance)) return instance;
    const credential = checkCredential(this.#credential);
    if (typeof credential !== 'string') return credential;

    let sessionId = options.sessionId;
    if (sessionId === undefined) {
      const url = new URL(`${CONSUMER_BASE_PATH}/sessions/lookup`, instance.origin);
      url.searchParams.set('provider', provider);
      url.searchParams.set('sessionId', nativeId);
      const reply = await get(url, credential, this.#timeoutMs, signal);
      const body = interpret(reply, 'session lookup', 'session-not-observed', NOT_OBSERVED);
      if (isFailure(body)) return body;
      const lookup = validateLookup(body.value);
      if (!lookup.ok) return invalid('session lookup', lookup.issues);
      sessionId = lookup.value.session.id;
    }

    const path = `${CONSUMER_BASE_PATH}/sessions/${encodeURIComponent(sessionId)}/report`;
    const reply = await get(new URL(path, instance.origin), credential, this.#timeoutMs, signal);
    const body = interpret(reply, 'session report', 'not-found', NO_REPORT);
    if (isFailure(body)) return body;
    const report = validateReport(body.value);
    if (!report.ok) return invalid('session report', report.issues);
    const native = report.value.session.native;
    if (native.provider !== provider || native.sessionId !== nativeId)
      return fail(
        'incompatible',
        'invalid_document',
        "Salidium's report names a different session than the one requested.",
      );
    return {
      availability: 'available',
      understanding: toUnderstanding(report.value, instance.discovery),
    };
  }
}

const NOT_OBSERVED =
  'Salidium has not observed this session. A session launched moments ago may not have reported yet.';
const NO_REPORT = 'Salidium holds no report for this session; it may have been deleted or expired.';

/**
 * Interprets the status of an authenticated request as the contract defines it. A 404 means "no
 * record" only when it carries the contract's error code for that request; any other answer the
 * contract does not give is incompatibility rather than a guess.
 */
function interpret(
  reply: Reply | null,
  what: string,
  missing: 'session-not-observed' | 'not-found',
  missingMessage: string,
): { value: unknown } | Failure {
  if (reply === null)
    return fail('unavailable', 'unreachable', `Salidium did not answer the ${what} request.`);
  if (reply.status === 200) return { value: reply.body };
  if (reply.status === 401)
    return fail(
      'unauthorized',
      'credential_rejected',
      'Salidium refused the consumer credential; it may have been revoked. Create a new one with `salidium consumer create <label>`.',
    );
  if (reply.status === 404) {
    const error = validateError(reply.body);
    if (error.ok && error.value.error === missing)
      return fail('not_found', 'not_observed', missingMessage);
  }
  if (reply.status >= 500)
    return fail(
      'unavailable',
      'server_error',
      `Salidium failed to answer the ${what} request (HTTP ${reply.status}).`,
    );
  return fail(
    'incompatible',
    'unexpected_status',
    `Salidium answered the ${what} request with HTTP ${reply.status}, which consumer contract v1 does not give.`,
  );
}
