import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, type Evaluation, EvaluationResult } from '@halcyonic/contracts';
import {
  type EvaluateOptions,
  SeorakClient,
  type SeorakOptions,
  seorakAgentFor,
} from './client.ts';
import {
  type CapturedSession,
  EXAMPLE,
  envelope,
  FakeSeorak,
  integrationToken,
  json,
  lensDocument,
  resolveDocument,
  resolveMiss,
  verificationRow,
} from './testing/fake-seorak.ts';

type Json = Record<string, unknown>;

const validateResult = compileValidator(EvaluationResult);

const CLAUDE = { kind: 'claude-agent', id: EXAMPLE.nativeSessionId };
const CODEX = { kind: 'codex', id: '0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47' };
const RESOLVE = '/api/v1/sessions/resolve';
const OUTCOME = `/api/v1/sessions/${EXAMPLE.sessionRef}/outcome`;
const LENS = `/api/v1/sessions/${EXAMPLE.sessionRef}/replay/verification`;

async function start(t: TestContext): Promise<FakeSeorak> {
  const fake = await FakeSeorak.start();
  t.after(() => fake.close());
  return fake;
}

function clientFor(fake: FakeSeorak, options: SeorakOptions = {}): SeorakClient {
  return new SeorakClient({ port: fake.port, timeoutMs: 2_000, ...options });
}

async function evaluate(
  fake: FakeSeorak,
  session: { kind: string; id: string } = CLAUDE,
  options: Partial<EvaluateOptions> & { client?: SeorakClient } = {},
): Promise<EvaluationResult> {
  const { client = clientFor(fake), credential = fake.token, signal } = options;
  const result = await client.evaluate(session.kind, session.id, {
    credential,
    ...(signal ? { signal } : {}),
  });
  const checked = validateResult(result);
  assert.ok(checked.ok, JSON.stringify(checked));
  return result;
}

function evaluation(result: EvaluationResult): Evaluation {
  if (result.availability !== 'available') assert.fail(JSON.stringify(result));
  return result.evaluation;
}

function reason(result: EvaluationResult): [string, string] {
  return result.availability === 'available'
    ? ['available', '']
    : [result.availability, result.reason.code];
}

/** The instant a rate limited answer names, in milliseconds. */
function retryAt(result: EvaluationResult): number {
  const message = result.availability === 'available' ? '' : result.reason.message;
  const instant = /until (\S+Z)/.exec(message)?.[1];
  assert.ok(instant, message);
  return Date.parse(instant);
}

/** Answers every request to `path` with `status`, a JSON body and the given headers. */
function answer(
  fake: FakeSeorak,
  path: string,
  status: number,
  body: unknown = {},
  headers: Record<string, string> = {},
): void {
  fake.overrides.set(path, (response) => {
    for (const [name, value] of Object.entries(headers)) response.setHeader(name, value);
    json(response, status, body);
  });
}

async function closedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return port;
}

describe('evaluating a session through Seorak', () => {
  test('resolves the native id in a POST body, then reads the outcome and verification lens', async (t) => {
    const fake = await start(t);
    const result = evaluation(await evaluate(fake));
    assert.deepEqual(result.source, { system: 'seorak', synthetic: false, api_version: 'v1' });

    const bearer = `Bearer ${fake.token}`;
    assert.deepEqual(
      fake.requests.map((request) => [request.method, request.path, request.headers.authorization]),
      [
        ['POST', RESOLVE, bearer],
        ['GET', OUTCOME, bearer],
        ['GET', LENS, bearer],
      ],
    );
    const [resolve] = fake.requests;
    assert.equal(resolve?.headers['content-type'], 'application/json');
    assert.deepEqual(JSON.parse(resolve?.body ?? ''), {
      agent: 'claude-code',
      nativeSessionId: EXAMPLE.nativeSessionId,
    });
    for (const request of fake.requests) {
      assert.equal(request.headers.host, `127.0.0.1:${fake.port}`);
      assert.equal(request.headers.origin, undefined);
      assert.equal(request.path.includes(EXAMPLE.nativeSessionId), false);
    }
  });

  test('maps Halcyonic runtime kinds to Seorak agents explicitly', () => {
    assert.equal(seorakAgentFor('claude-agent'), 'claude-code');
    assert.equal(seorakAgentFor('codex'), 'codex');
    for (const kind of ['claude-code', 'mock', 'opencode', 'Codex', 'toString', 'constructor', ''])
      assert.equal(seorakAgentFor(kind), null, kind);
  });

  test('resolves a Codex session by its thread id', async (t) => {
    const fake = await start(t);
    const session = fake.capture('codex', CODEX.id);
    assert.equal((await evaluate(fake, CODEX)).availability, 'available');
    assert.deepEqual(JSON.parse(fake.requests[0]?.body ?? ''), {
      agent: 'codex',
      nativeSessionId: CODEX.id,
    });
    assert.equal(fake.requests[1]?.path, `/api/v1/sessions/${session.ref}/outcome`);
  });

  test("labels the resolved session's cost as an estimate, with Seorak's statement about it", async (t) => {
    const fake = await start(t);
    assert.deepEqual(evaluation(await evaluate(fake)).cost, {
      availability: { state: 'available', reason: null },
      coverage: {
        requested: { from: '2026-06-28', through: '2026-09-26' },
        observed: { from: '2026-09-26', through: '2026-09-26' },
        matched_sessions: 1,
        included_sessions: 1,
        complete: true,
        omissions: [],
      },
      freshness: {
        state: 'fresh',
        generated_at: '2026-09-26T18:00:00.000Z',
        data_through: '2026-09-26T17:58:12.000Z',
        stale_at: '2026-09-26T18:05:00.000Z',
      },
      estimated_usd: 1.37,
      note: 'Estimated from token counts at list prices. Not a bill.',
    });
  });

  test('keeps an unpriced cost unknown, never zero', async (t) => {
    const fake = await start(t);
    (fake.example.resolve.session as Json).costUsd = null;
    const { cost } = evaluation(await evaluate(fake));
    assert.equal(cost.estimated_usd, null);
    assert.equal(cost.note, 'Estimated from token counts at list prices. Not a bill.');
  });

  test("maps ADR 007's example outcome and verification lens field by field", async (t) => {
    const fake = await start(t);
    const { outcome, verification } = evaluation(await evaluate(fake));
    assert.deepEqual(outcome.measure, {
      commits_landed: 1,
      uncommitted: {
        files_touched: 2,
        lines_added: 40,
        lines_removed: 6,
        generated_lines_excluded: 0,
      },
      line_survival: null,
      error_count: 3,
      first_error_at: '2026-09-26T17:44:10.000Z',
      end_reason: null,
    });
    assert.deepEqual(verification.lens, {
      by_kind: [{ label: 'test', runs: 4, passed: 3, pass_rate: 0.75 }],
      empty_reason: null,
    });
  });

  test('maps a matured line survival and an end reason', async (t) => {
    const fake = await start(t);
    const outcome = fake.example.outcome.outcome as Json;
    outcome.lineSurvival = {
      rung: '3d',
      fate: 'retained',
      rate: 0.92,
      linesAuthored: 50,
      linesSurviving: 46,
      commitsChecked: 3,
    };
    outcome.endReason = 'prompt_input_exit';
    const measure = evaluation(await evaluate(fake)).outcome.measure;
    assert.deepEqual(measure?.line_survival, {
      rung: '3d',
      fate: 'retained',
      rate: 0.92,
      lines_authored: 50,
      lines_surviving: 46,
      commits_checked: 3,
    });
    assert.equal(measure?.end_reason, 'prompt_input_exit');
  });

  test('keeps an outcome that is unavailable or has not matured as null, not zeros', async (t) => {
    const fake = await start(t);
    fake.example.outcome = {
      ...fake.example.outcome,
      availability: { state: 'unavailable', reason: 'not-yet-computed' },
      outcome: null,
    };
    const { outcome } = evaluation(await evaluate(fake));
    assert.equal(outcome.measure, null);
    assert.deepEqual(outcome.availability, { state: 'unavailable', reason: 'not_yet_computed' });

    const measured = await start(t);
    measured.example.outcome.outcome = {
      commitsLanded: null,
      uncommitted: null,
      lineSurvival: null,
      errorCount: 0,
      firstErrorAt: null,
      endReason: null,
    };
    const measure = evaluation(await evaluate(measured)).outcome.measure;
    assert.equal(measure?.commits_landed, null);
    assert.equal(measure?.error_count, 0);
  });

  test('maps the verification lens: one row per kind of check, its runs, passes and pass rate', async (t) => {
    const fake = await start(t);
    fake.example.lens = lensDocument(EXAMPLE.sessionRef, [
      verificationRow('test', 5, 4, 0.8),
      verificationRow('build', 1, 0, 0),
      verificationRow('lint', 0, 0, null),
    ]);
    assert.deepEqual(evaluation(await evaluate(fake)).verification.lens, {
      by_kind: [
        { label: 'test', runs: 5, passed: 4, pass_rate: 0.8 },
        { label: 'build', runs: 1, passed: 0, pass_rate: 0 },
        { label: 'lint', runs: 0, passed: 0, pass_rate: null },
      ],
      empty_reason: null,
    });
  });

  test("keeps Seorak's reason when it captured no verification run, or cannot produce the lens", async (t) => {
    const fake = await start(t);
    const lens = lensDocument(EXAMPLE.sessionRef, []);
    (lens.result as Json).emptyReason = 'No verification run was captured.';
    fake.example.lens = lens;
    assert.deepEqual(evaluation(await evaluate(fake)).verification.lens, {
      by_kind: [],
      empty_reason: 'No verification run was captured.',
    });

    const codex = await start(t);
    codex.capture('codex', CODEX.id).lens = {
      ...envelope(),
      availability: { state: 'unavailable', reason: 'not-captured' },
      result: null,
    };
    const { verification } = evaluation(await evaluate(codex, CODEX));
    assert.equal(verification.lens, null);
    assert.deepEqual(verification.availability, { state: 'unavailable', reason: 'not_captured' });
  });

  test('keeps availability, coverage and freshness per part, as Seorak stated each', async (t) => {
    const fake = await start(t);
    fake.example.outcome.availability = { state: 'partial', reason: 'result-limit' };
    fake.example.outcome.coverage = {
      requested: { from: '2026-06-28', through: '2026-09-26' },
      observed: null,
      matchedSessionCount: 1,
      includedSessionCount: 0,
      complete: false,
      omissions: ['projection-pending', 'credential-restriction'],
    };
    fake.example.lens.freshness = {
      state: 'revalidating',
      generatedAt: '2026-09-26T18:01:00.000Z',
      dataThrough: null,
      staleAt: '2026-09-26T18:02:00.000Z',
    };
    const { cost, outcome, verification } = evaluation(await evaluate(fake));
    assert.deepEqual(cost.availability, { state: 'available', reason: null });
    assert.deepEqual(outcome.availability, { state: 'partial', reason: 'result_limit' });
    assert.deepEqual(outcome.coverage, {
      requested: { from: '2026-06-28', through: '2026-09-26' },
      observed: null,
      matched_sessions: 1,
      included_sessions: 0,
      complete: false,
      omissions: ['projection_pending', 'credential_restriction'],
    });
    assert.equal(outcome.freshness.state, 'fresh');
    assert.deepEqual(verification.freshness, {
      state: 'revalidating',
      generated_at: '2026-09-26T18:01:00.000Z',
      data_through: null,
      stale_at: '2026-09-26T18:02:00.000Z',
    });
  });

  test('passes a stale answer on as stale, with the instant it went stale', async (t) => {
    const fake = await start(t);
    fake.example.resolve.freshness = {
      state: 'stale',
      generatedAt: '2026-09-26T20:00:00+02:00',
      dataThrough: '2026-09-26T17:58:12.5Z',
      staleAt: '2026-09-26T18:05:00Z',
    };
    const { cost } = evaluation(await evaluate(fake));
    // Instants are normalized to Halcyonic's form; the moment each names is unchanged.
    assert.deepEqual(cost.freshness, {
      state: 'stale',
      generated_at: '2026-09-26T18:00:00.000Z',
      data_through: '2026-09-26T17:58:12.500Z',
      stale_at: '2026-09-26T18:05:00.000Z',
    });
    assert.ok(Date.parse(cost.freshness.stale_at) < Date.now());
  });

  test('reads a value v1 added to a union later as unknown, never as incompatible', async (t) => {
    const fake = await start(t);
    const { example } = fake;
    example.outcome.availability = { state: 'partial', reason: 'sampled' };
    example.outcome.coverage = {
      ...(example.outcome.coverage as Json),
      complete: true,
      omissions: ['projection-pending', 'sampled', 'thinned'],
    };
    Object.assign(example.outcome.outcome as Json, {
      endReason: 'crashed',
      lineSurvival: {
        rung: '3d',
        fate: 'reverted',
        rate: null,
        linesAuthored: 12,
        linesSurviving: 0,
        commitsChecked: 1,
      },
    });
    const row = verificationRow('test', 4, 3, 0.75);
    (row.metrics as Json[])[2] = { key: 'passRate', label: 'Pass rate', value: 75, unit: 'ratio' };
    example.lens = lensDocument(example.ref, [row]);

    const { outcome, verification } = evaluation(await evaluate(fake));
    assert.deepEqual(outcome.availability, { state: 'partial', reason: 'unknown' });
    // An omission the client cannot name means the coverage is not known to be complete.
    assert.equal(outcome.coverage.complete, false);
    assert.deepEqual(outcome.coverage.omissions, ['projection_pending', 'unknown']);
    assert.equal(outcome.measure?.end_reason, 'unknown');
    assert.equal(outcome.measure?.line_survival?.fate, 'unknown');
    // A metric in a unit the client does not know is unknown; the rest of the row stands.
    assert.deepEqual(verification.lens?.by_kind, [
      { label: 'test', runs: 4, passed: 3, pass_rate: null },
    ]);
  });

  test('says not_found when Seorak has not captured the session, which may still change', async (t) => {
    const fake = await start(t);
    const result = await evaluate(fake, { kind: 'claude-agent', id: 'launched-a-moment-ago' });
    assert.deepEqual(reason(result), ['not_found', 'not_captured']);
    assert.match(result.availability === 'not_found' ? result.reason.message : '', /retry/);
    assert.deepEqual(
      fake.requests.map((request) => request.path),
      [RESOLVE],
    );
  });

  test('reads what a resolve without a session means from its reason', async (t) => {
    const fake = await start(t);
    for (const [seorak, expected] of [
      ['not-retained', ['not_found', 'not_retained']],
      ['not-yet-computed', ['not_found', 'not_yet_computed']],
      ['outside-credential-restriction', ['unauthorized', 'outside_credential_restriction']],
      ['temporarily-unavailable', ['unavailable', 'temporarily_unavailable']],
      ['result-limit', ['unavailable', 'result_limit']],
      // A reason v1 added later: unavailable for a reason the client cannot name (ADR 007).
      ['paused-by-owner', ['unavailable', 'unknown_reason']],
    ] as const) {
      fake.example.resolve = {
        ...resolveMiss(),
        availability: { state: 'unavailable', reason: seorak },
      };
      assert.deepEqual(reason(await evaluate(fake)), expected, seorak);
    }
  });

  test('says not_found without a request for an id Seorak cannot resolve', async (t) => {
    const fake = await start(t);
    for (const id of ['', '-leading-dash', 'has space', 'line\nbreak', 'x'.repeat(257), 'café'])
      assert.deepEqual(reason(await evaluate(fake, { kind: 'codex', id })), [
        'not_found',
        'not_resolvable',
      ]);
    assert.deepEqual(fake.requests, []);
  });

  test('says unavailable, without a request or a credential, for a runtime Seorak does not observe', async (t) => {
    const fake = await start(t);
    for (const kind of ['mock', 'opencode', 'claude-code']) {
      const result = await evaluate(fake, { kind, id: 'session-1' }, { credential: null });
      assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    }
    assert.deepEqual(fake.requests, []);
  });

  test('says unavailable when the local plane is not running', async (t) => {
    const fake = await start(t);
    const client = new SeorakClient({ port: await closedPort(), timeoutMs: 2_000 });
    assert.deepEqual(reason(await evaluate(fake, CLAUDE, { client })), [
      'unavailable',
      'not_running',
    ]);
  });

  test('gives up on a request Seorak does not answer in time', async (t) => {
    const fake = await start(t);
    fake.overrides.set(RESOLVE, () => {});
    const result = await evaluate(fake, CLAUDE, { client: clientFor(fake, { timeoutMs: 100 }) });
    assert.deepEqual(reason(result), ['unavailable', 'unreachable']);
  });

  test('rejects when the caller aborts, which is not a state of Seorak', async (t) => {
    const fake = await start(t);
    fake.overrides.set(RESOLVE, () => {});
    const controller = new AbortController();
    const pending = evaluate(fake, CLAUDE, { signal: controller.signal });
    setTimeout(() => controller.abort(), 20);
    await assert.rejects(pending, { name: 'AbortError' });
  });

  test('says unavailable when Seorak fails to answer', async (t) => {
    const fake = await start(t);
    answer(fake, OUTCOME, 503);
    assert.deepEqual(reason(await evaluate(fake)), ['unavailable', 'server_error']);
  });

  test('says unauthorized without sending a missing or malformed credential', async (t) => {
    const fake = await start(t);
    assert.deepEqual(reason(await evaluate(fake, CLAUDE, { credential: null })), [
      'unauthorized',
      'credential_missing',
    ]);
    const salidium = `salidium_consumer_${'a'.repeat(12)}_${'b'.repeat(64)}`;
    for (const credential of ['nope', 'srkx_', `${fake.token}\n`, `Bearer ${fake.token}`, salidium])
      assert.deepEqual(reason(await evaluate(fake, CLAUDE, { credential })), [
        'unauthorized',
        'credential_malformed',
      ]);
    assert.deepEqual(fake.requests, []);
  });

  test('says unauthorized when Seorak refuses the credential (401)', async (t) => {
    const fake = await start(t);
    const result = await evaluate(fake, CLAUDE, { credential: integrationToken() });
    assert.deepEqual(reason(result), ['unauthorized', 'credential_rejected']);
    assert.equal(fake.requests.length, 1);
  });

  test('says unauthorized, naming the scope, when the credential lacks one a read needs (403)', async (t) => {
    const fake = await start(t);
    fake.scopes = new Set(['sessions:read']);
    const withoutReplay = await evaluate(fake);
    assert.deepEqual(reason(withoutReplay), ['unauthorized', 'credential_forbidden']);
    assert.match(
      withoutReplay.availability === 'unauthorized' ? withoutReplay.reason.message : '',
      /verification lens .* replay:read scope/,
    );

    fake.scopes = new Set(['replay:read']);
    const withoutSessions = await evaluate(fake);
    assert.deepEqual(reason(withoutSessions), ['unauthorized', 'credential_forbidden']);
    assert.match(
      withoutSessions.availability === 'unauthorized' ? withoutSessions.reason.message : '',
      /session resolve .* sessions:read scope/,
    );
  });

  test('honors a 429 and its Retry-After: no request until the instant Seorak named', async (t) => {
    const fake = await start(t);
    const client = clientFor(fake);
    answer(fake, OUTCOME, 429, { error: 'rate_limited' }, { 'Retry-After': '30' });
    const before = Date.now();
    const refused = await evaluate(fake, CLAUDE, { client });
    assert.deepEqual(reason(refused), ['unavailable', 'rate_limited']);
    const until = retryAt(refused);
    assert.ok(until >= before + 29_000 && until <= Date.now() + 30_000, String(until - before));
    assert.equal(fake.requests.length, 2);

    fake.overrides.clear();
    const held = await evaluate(fake, CLAUDE, { client });
    assert.deepEqual(reason(held), ['unavailable', 'rate_limited']);
    assert.equal(retryAt(held), until);
    assert.equal(fake.requests.length, 2);
  });

  test('reads Retry-After as an HTTP date, waits a minute without one, and an hour at most', async (t) => {
    const fake = await start(t);
    const date = new Date(Math.ceil(Date.now() / 1000) * 1000 + 120_000);
    answer(fake, RESOLVE, 429, {}, { 'Retry-After': date.toUTCString() });
    assert.equal(retryAt(await evaluate(fake)), date.getTime());

    for (const [header, delay] of [
      [undefined, 60_000],
      ['soon', 60_000],
      ['99999999999999999999', 3_600_000],
      ['Fri, 31 Dec 9999 23:59:59 GMT', 3_600_000],
    ] as const) {
      answer(fake, RESOLVE, 429, {}, header === undefined ? {} : { 'Retry-After': header });
      const before = Date.now();
      const until = retryAt(await evaluate(fake));
      assert.ok(until >= before + delay && until <= Date.now() + delay, `${header}`);
    }
  });

  test('throttles on the client: no more requests in a rolling minute than the budget', async (t) => {
    const fake = await start(t);
    const client = clientFor(fake);
    for (let evaluation = 0; evaluation < 20; evaluation++)
      assert.equal((await evaluate(fake, CLAUDE, { client })).availability, 'available');
    assert.equal(fake.requests.length, 60);
    const held = await evaluate(fake, CLAUDE, { client });
    assert.deepEqual(reason(held), ['unavailable', 'rate_limited']);
    assert.equal(fake.requests.length, 60);
    // The first request of the minute frees the first slot a minute after it started.
    assert.ok(retryAt(held) > Date.now() + 55_000);
  });

  test('reserves a whole evaluation before its first request and releases what it did not need', async (t) => {
    const fake = await start(t);
    const client = clientFor(fake, { requestsPerMinute: 6 });
    const missed = { kind: 'claude-agent', id: 'not-captured-yet' };
    for (let attempt = 0; attempt < 3; attempt++)
      assert.deepEqual(reason(await evaluate(fake, missed, { client })), [
        'not_found',
        'not_captured',
      ]);
    assert.equal((await evaluate(fake, CLAUDE, { client })).availability, 'available');
    assert.equal(fake.requests.length, 6);
    assert.deepEqual(reason(await evaluate(fake, missed, { client })), [
      'unavailable',
      'rate_limited',
    ]);
    assert.equal(fake.requests.length, 6);
  });

  test('says incompatible for an answer the contract does not give', async (t) => {
    const fake = await start(t);
    for (const status of [404, 400, 418]) {
      answer(fake, RESOLVE, status, { error: 'not_found' });
      assert.deepEqual(
        reason(await evaluate(fake)),
        ['incompatible', 'unexpected_status'],
        `${status}`,
      );
    }
  });

  test('says incompatible when a document does not match integration API v1', async (t) => {
    const notJson = (fake: FakeSeorak) =>
      fake.overrides.set(RESOLVE, (response) => response.end('{"apiVersion": "v1", '));
    const cases: [string, (session: CapturedSession, fake: FakeSeorak) => void, RegExp][] = [
      ['a body that is not JSON', (_s, fake) => notJson(fake), /resolve answer/],
      ['another API version', (s) => (s.resolve.apiVersion = 'v2'), /apiVersion/],
      ['no freshness', (s) => delete s.outcome.freshness, /required properties freshness/],
      [
        'an availability state outside the published ones, which v1 does not grow',
        (s) => (s.lens.availability = { state: 'maybe', reason: null }),
        /availability\/state/,
      ],
      ['a negative cost', (s) => ((s.resolve.session as Json).costUsd = -1), /costUsd/],
      [
        'an instant that is not one',
        (s) => ((s.outcome.freshness as Json).staleAt = 'soon'),
        /staleAt/,
      ],
      [
        'a row without a pass rate',
        (s) => {
          const row = verificationRow('test', 5, 4, 0.8);
          row.metrics = (row.metrics as Json[]).slice(0, 2);
          s.lens = lensDocument(s.ref, [row]);
        },
        /has no passRate metric/,
      ],
      [
        'a pass rate given as a percentage rather than a fraction',
        (s) => (s.lens = lensDocument(s.ref, [verificationRow('test', 5, 4, 80)])),
        /metrics\/2\/value must be a fraction/,
      ],
      [
        'runs in another published unit',
        (s) => {
          const row = verificationRow('test', 5, 4, 0.8);
          (row.metrics as Json[])[0] = { key: 'runs', label: 'Runs', value: 5, unit: 'seconds' };
          s.lens = lensDocument(s.ref, [row]);
        },
        /metrics\/0\/unit must be count for runs/,
      ],
      [
        'more passes than runs',
        (s) => (s.lens = lensDocument(s.ref, [verificationRow('test', 2, 3, 1)])),
        /more passed runs than runs/,
      ],
      [
        "a label beyond Halcyonic's bound",
        (s) => (s.lens = lensDocument(s.ref, [verificationRow('t'.repeat(121), 1, 1, 1)])),
        /label/,
      ],
    ];
    for (const [name, spoil, detail] of cases) {
      const spoiled = await start(t);
      spoil(spoiled.example, spoiled);
      const result = await evaluate(spoiled);
      assert.deepEqual(reason(result), ['incompatible', 'invalid_document'], name);
      assert.match(
        result.availability === 'incompatible' ? result.reason.message : '',
        detail,
        name,
      );
    }
  });

  test('refuses an answer that contradicts the request or itself', async (t) => {
    const cases: [string, (fake: FakeSeorak) => void][] = [
      [
        'a session of another agent',
        (f) => (f.example.resolve = resolveDocument('codex', f.example.ref)),
      ],
      [
        'an outcome of another session',
        (f) => (f.example.outcome.sessionRef = `ses_${'0'.repeat(32)}`),
      ],
      [
        'a lens of another session',
        (f) => (f.example.lens = lensDocument(`ses_${'0'.repeat(32)}`)),
      ],
      [
        'a miss that says it is available',
        (f) =>
          (f.example.resolve = {
            ...resolveMiss(),
            availability: { state: 'available', reason: null },
          }),
      ],
      [
        'a lens that says it is unavailable',
        (f) => (f.example.lens.availability = { state: 'unavailable', reason: 'not-captured' }),
      ],
      // Available means non-null: only `unavailable` carries a null payload (ADR 007).
      ['an available outcome without a measure', (f) => (f.example.outcome.outcome = null)],
      [
        'a partial lens without a result',
        (f) =>
          (f.example.lens = {
            ...f.example.lens,
            availability: { state: 'partial', reason: 'result-limit' },
            result: null,
          }),
      ],
    ];
    for (const [name, spoil] of cases) {
      const fake = await start(t);
      spoil(fake);
      assert.deepEqual(reason(await evaluate(fake)), ['incompatible', 'invalid_document'], name);
    }
  });

  test('ignores what a later v1 adds', async (t) => {
    const fake = await start(t);
    const { resolve, outcome, lens } = fake.example;
    Object.assign(resolve, { addedLater: { any: 'shape' } });
    Object.assign(resolve.session as Json, { addedLater: 1 });
    Object.assign(resolve.coverage as Json, { addedLater: [] });
    Object.assign(outcome.outcome as Json, { addedLater: true });
    const result = lens.result as { rows: Json[] };
    result.rows.push({
      ...verificationRow('build', 1, 1, 1),
      share: 0.5,
      metrics: [
        ...(verificationRow('build', 1, 1, 1).metrics as Json[]),
        { key: 'addedLater', label: 'Later', value: true, unit: 'none', tone: 'positive' },
      ],
    });
    assert.equal((await evaluate(fake)).availability, 'available');
  });

  test('refuses a budget smaller than one evaluation and a port that is not one', () => {
    assert.throws(() => new SeorakClient({ requestsPerMinute: 2 }), RangeError);
    assert.throws(() => new SeorakClient({ port: 70_000 }), RangeError);
  });
});
