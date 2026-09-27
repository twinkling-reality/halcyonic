import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import {
  createServer,
  type IncomingHttpHeaders,
  type IncomingMessage,
  type ServerResponse,
} from 'node:http';
import type { AddressInfo } from 'node:net';

const FIXTURES = new URL('../../fixtures/v1/', import.meta.url);

type Json = Record<string, unknown>;

function fixture(name: string): Json {
  return JSON.parse(readFileSync(new URL(`${name}.json`, FIXTURES), 'utf8')) as Json;
}

/** ADR 007's example of a resolve that found its session, word for word. Always a fresh copy. */
export function resolveHit(): Json {
  return fixture('resolve-hit');
}

/** ADR 007's example outcome of the session its resolve example finds. Always a fresh copy. */
export function outcomeExample(): Json {
  return fixture('outcome');
}

/** ADR 007's example verification lens of that session. Always a fresh copy. */
export function lensExample(): Json {
  return fixture('lens-verification');
}

/** The session ADR 007's example resolves. */
export const EXAMPLE = {
  agent: 'claude-code',
  nativeSessionId: '0f7c1a52-3b9e-4d1a-9f2e-6c0b8d4e1a27',
  sessionRef: 'ses_6b1f0c2d9e8a47f3b5c4d2e1f0a9b8c7',
} as const;

/** A credential with Seorak's `srkx_` prefix, invented for one test. */
export function integrationToken(): string {
  return `srkx_${randomBytes(32).toString('base64url')}`;
}

/** A session reference in the published pattern, invented for one test. */
export function sessionRef(): string {
  return `ses_${randomBytes(16).toString('hex')}`;
}

/** The v1 envelope of ADR 007's example: available, complete and fresh. */
export function envelope(): Json {
  const { apiVersion, availability, coverage, freshness } = resolveHit();
  return { apiVersion, availability, coverage, freshness };
}

/** A resolve that found a session of `agent`, built on ADR 007's example. */
export function resolveDocument(agent: string, ref: string): Json {
  const hit = resolveHit();
  return { ...hit, session: { ...(hit.session as Json), agent, sessionRef: ref } };
}

/**
 * ADR 007's miss, as its text describes it: the same envelope, unavailable because the session is
 * not captured, with no observed range, no `dataThrough`, no matched or included session, and a
 * null session.
 */
export function resolveMiss(): Json {
  const { coverage, freshness } = envelope() as { coverage: Json; freshness: Json };
  return {
    ...envelope(),
    availability: { state: 'unavailable', reason: 'not-captured' },
    coverage: {
      ...coverage,
      observed: null,
      matchedSessionCount: 0,
      includedSessionCount: 0,
      complete: false,
    },
    freshness: { ...freshness, dataThrough: null },
    session: null,
  };
}

/**
 * A `PrivateOutcomeDto`, built from the published type: two commits landed, some uncommitted work,
 * one errored tool call, and no line survival yet, because the three day rung has not matured.
 */
export function outcomeDocument(ref: string): Json {
  return {
    ...envelope(),
    sessionRef: ref,
    outcome: {
      commitsLanded: 2,
      uncommitted: {
        filesTouched: 3,
        linesAdded: 41,
        linesRemoved: 7,
        generatedLinesExcluded: 120,
      },
      lineSurvival: null,
      errorCount: 1,
      firstErrorAt: '2026-09-26T17:44:10.000Z',
      endReason: null,
    },
  };
}

/**
 * One row of the verification lens as ADR 007 (section 2) states it: a kind of check with `runs`
 * and `passed` in the unit `count`, and `passRate` in the unit `percent` whose value is a fraction.
 * The metric labels are the ADR example's.
 */
export function verificationRow(
  label: string,
  runs: number | null,
  passed: number | null,
  passRate: number | null,
): Json {
  return {
    label,
    metrics: [
      { key: 'runs', label: 'Measured runs', value: runs, unit: 'count' },
      { key: 'passed', label: 'Passed runs', value: passed, unit: 'count' },
      { key: 'passRate', label: 'Pass rate', value: passRate, unit: 'percent' },
    ],
  };
}

/** A `PrivateReplayLensDto` for the verification lens of one session, built from the published types. */
export function lensDocument(ref: string, rows: Json[] = defaultRows()): Json {
  return {
    ...envelope(),
    result: {
      lens: 'verification',
      level: 'session',
      target: { kind: 'session', sessionRef: ref },
      headline: null,
      rows,
      emptyReason: null,
      loadedSessionCount: 1,
      momentCount: 7,
      nextCursor: null,
    },
  };
}

function defaultRows(): Json[] {
  return [verificationRow('test', 5, 4, 0.8), verificationRow('typecheck', 2, 2, 1)];
}

export interface RecordedRequest {
  readonly method: string;
  /** The path and query. */
  readonly path: string;
  readonly headers: IncomingHttpHeaders;
  readonly body: string;
}

/** A session the fake plane has captured, and the documents it answers about it. */
export interface CapturedSession {
  readonly agent: string;
  readonly nativeSessionId: string;
  readonly ref: string;
  resolve: Json;
  outcome: Json;
  lens: Json;
}

const RESOLVABLE_AGENTS = new Set(['claude-code', 'codex']);
const NATIVE_SESSION_ID = /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/;

/**
 * A stand-in for Seorak's local plane that serves integration API v1 over real loopback HTTP as the
 * public sources describe it. The Host must name 127.0.0.1 at the listener's port and an Origin is
 * refused (the collector's README). Every read needs the one `srkx_` bearer the plane issued, or it
 * answers 401 with `error="invalid_token"`, and the scope of the read, or it answers 403 with
 * `error="insufficient_scope"` and the scope (ADR 002 and 007). A resolve is a JSON POST of exactly
 * `agent` and `nativeSessionId`, refused with 413 over 1 KiB, 415 for another media type and 400
 * for another shape, and a miss is the v1 envelope with a null session (ADR 007). The session of
 * ADR 007's examples is captured from the start, answered with those examples.
 *
 * Error bodies are not part of the contract. The 401's copies what the running plane answered on
 * 2026-09-26; the others are made up. The client reads none of them.
 */
export class FakeSeorak {
  /** The one credential this plane accepts. */
  token = integrationToken();
  /** The scopes that credential carries. */
  scopes = new Set(['sessions:read', 'replay:read']);
  readonly requests: RecordedRequest[] = [];
  readonly sessions: CapturedSession[] = [];
  /** Replaces the answer to one path. */
  readonly overrides = new Map<string, (response: ServerResponse) => void>();
  readonly #server = createServer((request, response) => this.#receive(request, response));
  #port = 0;

  static async start(): Promise<FakeSeorak> {
    const fake = new FakeSeorak();
    const example = fake.capture(EXAMPLE.agent, EXAMPLE.nativeSessionId, resolveHit());
    example.outcome = outcomeExample();
    example.lens = lensExample();
    await new Promise<void>((resolve) => fake.#server.listen(0, '127.0.0.1', resolve));
    fake.#port = (fake.#server.address() as AddressInfo).port;
    return fake;
  }

  get port(): number {
    return this.#port;
  }

  /** Captures a session, answered with `resolve` and the made-up outcome and lens documents. */
  capture(
    agent: string,
    nativeSessionId: string,
    resolve: Json = resolveDocument(agent, sessionRef()),
  ): CapturedSession {
    const ref = (resolve.session as Json).sessionRef as string;
    const session = {
      agent,
      nativeSessionId,
      ref,
      resolve,
      outcome: outcomeDocument(ref),
      lens: lensDocument(ref),
    };
    this.sessions.push(session);
    return session;
  }

  /** The captured session behind ADR 007's example. */
  get example(): CapturedSession {
    const [example] = this.sessions;
    if (example === undefined) throw new Error('the example session was not captured');
    return example;
  }

  async close(): Promise<void> {
    const closed = new Promise<void>((resolve) => this.#server.close(() => resolve()));
    // fetch keeps connections alive; close them rather than wait for their idle timeout.
    this.#server.closeAllConnections();
    await closed;
  }

  #receive(request: IncomingMessage, response: ServerResponse): void {
    const chunks: Buffer[] = [];
    request.on('data', (chunk: Buffer) => chunks.push(chunk));
    request.on('end', () =>
      this.#handle(request, Buffer.concat(chunks).toString('utf8'), response),
    );
  }

  #handle(request: IncomingMessage, body: string, response: ServerResponse): undefined {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    const method = request.method ?? '';
    this.requests.push({
      method,
      path: `${url.pathname}${url.search}`,
      headers: request.headers,
      body,
    });
    if (request.headers.host !== `127.0.0.1:${this.#port}` || request.headers.origin !== undefined)
      return json(response, 403, { error: 'forbidden' });
    const override = this.overrides.get(url.pathname);
    if (override) {
      override(response);
      return;
    }
    if (request.headers.authorization !== `Bearer ${this.token}`) {
      response.setHeader('WWW-Authenticate', 'Bearer error="invalid_token"');
      return json(response, 401, { error: 'unauthorized' });
    }
    if (method === 'POST' && url.pathname === '/api/v1/sessions/resolve')
      return this.#resolve(request, body, response);
    const read = /^\/api\/v1\/sessions\/(ses_[0-9a-f]{32})\/(outcome|replay\/verification)$/.exec(
      url.pathname,
    );
    if (method !== 'GET' || read === null) return json(response, 404, { error: 'not_found' });
    const [, ref, what] = read;
    const scope = what === 'outcome' ? 'sessions:read' : 'replay:read';
    if (!this.scopes.has(scope)) return insufficientScope(response, scope);
    const session = this.sessions.find((candidate) => candidate.ref === ref);
    if (session === undefined) return json(response, 404, { error: 'not_found' });
    return json(response, 200, what === 'outcome' ? session.outcome : session.lens);
  }

  #resolve(request: IncomingMessage, body: string, response: ServerResponse): undefined {
    if (!this.scopes.has('sessions:read')) return insufficientScope(response, 'sessions:read');
    if (Buffer.byteLength(body) > 1024) return json(response, 413, { error: 'too_large' });
    if (!(request.headers['content-type'] ?? '').startsWith('application/json'))
      return json(response, 415, { error: 'unsupported_media_type' });
    let input: unknown;
    try {
      input = JSON.parse(body);
    } catch {
      return json(response, 400, { error: 'bad_request' });
    }
    // As the published `parsePrivateSessionResolveInput`: exactly two keys, a resolvable agent, a
    // native id in the published pattern.
    const { agent, nativeSessionId } = (input ?? {}) as Json;
    if (
      typeof input !== 'object' ||
      Array.isArray(input) ||
      Object.keys(input as Json).length !== 2 ||
      typeof agent !== 'string' ||
      !RESOLVABLE_AGENTS.has(agent) ||
      typeof nativeSessionId !== 'string' ||
      !NATIVE_SESSION_ID.test(nativeSessionId)
    )
      return json(response, 400, { error: 'bad_request' });
    const session = this.sessions.find(
      (candidate) => candidate.agent === agent && candidate.nativeSessionId === nativeSessionId,
    );
    return json(response, 200, session === undefined ? resolveMiss() : session.resolve);
  }
}

export function json(response: ServerResponse, status: number, body: unknown): undefined {
  response.statusCode = status;
  response.setHeader('Content-Type', 'application/json; charset=utf-8');
  response.setHeader('Cache-Control', 'no-store');
  response.end(JSON.stringify(body));
}

function insufficientScope(response: ServerResponse, scope: string): undefined {
  response.setHeader('WWW-Authenticate', `Bearer error="insufficient_scope", scope="${scope}"`);
  return json(response, 403, { error: 'forbidden' });
}
