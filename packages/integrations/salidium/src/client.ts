import type { UnderstandingFailure, UnderstandingResult } from '@halcyonic/contracts';
import {
  checkCredential,
  connect,
  credentialRejected,
  DEFAULT_TIMEOUT_MS,
  type Failure,
  fail,
  get,
  invalid,
  isFailure,
  type Reply,
  refusal,
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

/** The provider ids Salidium's consumer contract uses for the runtimes it may observe. */
export type SalidiumProvider = 'claude-code' | 'codex' | 'salidium/opencode';

/**
 * Halcyonic runtime kinds whose sessions Salidium may observe, with the provider id Salidium uses
 * for each. A runtime kind is Halcyonic's adapter type and a provider id is Salidium's vocabulary,
 * so the mapping is written out rather than assumed: the Claude Agent adapter (kind `claude-agent`)
 * runs Claude Code sessions, which Salidium names `claude-code`. Every other kind, such as `mock`,
 * is not observed by Salidium.
 */
const PROVIDER_BY_RUNTIME_KIND: ReadonlyMap<string, SalidiumProvider> = new Map([
  ['claude-agent', 'claude-code'],
  ['codex', 'codex'],
  ['opencode', 'salidium/opencode'],
]);

/**
 * Providers Salidium observes only where a person turned them on, as its OpenCode provider ships
 * (Salidium 0.7.0, consumer contract 1.1, unreleased when written): asked about only when the
 * running daemon lists them, never on a guess from its version.
 */
const DECLARED_ONLY: ReadonlySet<SalidiumProvider> = new Set(['salidium/opencode']);

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
  readonly #credential: SalidiumOptions['credential'];
  readonly #timeoutMs: number;

  constructor(options: SalidiumOptions) {
    this.#home = options.home;
    this.#credential = options.credential;
    this.#timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  }
  /** The credential as configured, read now when it is read on demand. */
  #configured(): string | null | UnderstandingFailure {
    return typeof this.#credential === 'function' ? this.#credential() : this.#credential;
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
    const notObserved = fail(
      'unavailable',
      'runtime_not_observed',
      `Salidium does not observe sessions of the ${runtimeKind} runtime.`,
    );
    if (provider === null) return notObserved;
    if (!NATIVE_SESSION_ID_PATTERN.test(nativeId))
      return fail(
        'not_found',
        'not_observable',
        "Salidium's contract cannot name a session id with control characters or over 512 characters.",
      );
    const { signal } = options;
    // A provider asked about only where the daemon says it observes it: without a daemon that says
    // so, the answer is what it always was, and the credential is not read for it. For the others,
    // a missing credential is said first, as it always was.
    const declaredOnly = DECLARED_ONLY.has(provider);
    let configured = declaredOnly ? null : this.#configured();
    if (configured !== null && typeof configured !== 'string') return configured;
    const instance = await connect(this.#home, this.#timeoutMs, signal);
    if (isFailure(instance)) return declaredOnly ? notObserved : instance;
    if (instance.providers === null ? declaredOnly : !instance.providers.has(provider))
      return instance.providers === null
        ? notObserved
        : fail(
            'unavailable',
            'runtime_not_observed',
            `Salidium is not observing sessions of the ${runtimeKind} runtime now; it lists the ones it does.`,
          );
    if (declaredOnly) configured = this.#configured();
    if (configured !== null && typeof configured !== 'string') return configured;
    const credential = checkCredential(configured);
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
      understanding: toUnderstanding(report.value, instance),
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
  if (reply.status === 401) return credentialRejected();
  if (reply.status === 404) {
    const error = validateError(reply.body);
    if (error.ok && error.value.error === missing)
      return fail('not_found', 'not_observed', missingMessage);
  }
  return (
    refusal(reply.status, reply.body, what) ??
    fail(
      'incompatible',
      'unexpected_status',
      `Salidium answered the ${what} request with HTTP ${reply.status}, which consumer contract v1 does not give.`,
    )
  );
}
