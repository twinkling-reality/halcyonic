import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  cadenceRow,
  EXAMPLE,
  lensDocument,
  lensExample,
  otherLensDocument,
  outcomeDocument,
  outcomeExample,
  resolveHit,
  resolveMiss,
  toolMixRow,
  verificationRow,
} from './testing/fake-seorak.ts';
import {
  CREDENTIAL_PATTERN,
  NATIVE_SESSION_ID_PATTERN,
  readCadenceLens,
  readLens,
  readToolMixLens,
  validateOutcome,
  validateSession,
} from './wire.ts';

type Json = Record<string, unknown>;

function issues(result: { ok: boolean; issues?: { path: string; message: string }[] }): string[] {
  return result.ok ? [] : (result.issues ?? []).map((issue) => `${issue.path} ${issue.message}`);
}

describe('integration API v1 reader', () => {
  test("accepts ADR 007's resolve example word for word, and the miss it describes", () => {
    const hit = validateSession(resolveHit());
    assert.ok(hit.ok);
    assert.deepEqual(hit.value.session, {
      sessionRef: EXAMPLE.sessionRef,
      projectRef: 'prj_0a1b2c3d4e5f60718293a4b5c6d7e8f9',
      agent: 'claude-code',
      status: 'active',
      startedAt: '2026-09-26T17:40:03.000Z',
      endedAt: null,
      elapsedSeconds: 1089,
      toolCallCount: 42,
      promptCount: null,
      costUsd: 1.37,
      launcher: 'my-launcher',
    });
    assert.equal(validateSession(resolveMiss()).ok, true);
  });

  test("accepts ADR 007's example outcome and verification lens", () => {
    const outcome = validateOutcome(outcomeExample());
    assert.ok(outcome.ok);
    assert.equal(outcome.value.sessionRef, EXAMPLE.sessionRef);
    const lens = readLens(lensExample());
    assert.ok(lens.ok);
    assert.deepEqual(lens.value.result?.rows, [
      { label: 'test', runs: 4, passed: 3, passRate: 0.75 },
    ]);
  });

  test('accepts outcome and lens documents built from the published types', () => {
    assert.equal(validateOutcome(outcomeDocument(EXAMPLE.sessionRef)).ok, true);
    const lens = readLens(lensDocument(EXAMPLE.sessionRef));
    assert.ok(lens.ok);
    assert.deepEqual(lens.value.result?.rows, [
      { label: 'test', runs: 5, passed: 4, passRate: 0.8 },
      { label: 'typecheck', runs: 2, passed: 2, passRate: 1 },
    ]);
  });

  test('reads a pass rate of null, as when nothing ran, and a row of no kind', () => {
    const lens = readLens(
      lensDocument(EXAMPLE.sessionRef, [verificationRow('Verification', 0, 0, null)]),
    );
    assert.ok(lens.ok);
    assert.deepEqual(lens.value.result?.rows, [
      { label: 'Verification', runs: 0, passed: 0, passRate: null },
    ]);
  });

  test('reads the five unions v1 may grow as any string', () => {
    const outcome = outcomeExample();
    const measure = outcome.outcome as Json;
    const grown = {
      ...outcome,
      availability: { state: 'partial', reason: 'sampled' },
      coverage: { ...(outcome.coverage as Json), omissions: ['sampled'] },
      outcome: {
        ...measure,
        endReason: 'crashed',
        lineSurvival: {
          rung: '3d',
          fate: 'reverted',
          rate: null,
          linesAuthored: 1,
          linesSurviving: 0,
          commitsChecked: 1,
        },
      },
    };
    assert.deepEqual(issues(validateOutcome(grown)), []);
    const row = verificationRow('test', 1, 1, 1);
    (row.metrics as Json[])[2] = { key: 'passRate', label: 'Pass rate', value: 100, unit: 'ratio' };
    const lens = readLens(lensDocument(EXAMPLE.sessionRef, [row]));
    assert.ok(lens.ok);
    assert.equal(lens.value.result?.rows[0]?.passRate, null);
  });

  test('rejects a missing property, a wrong type, an unknown value and an omitted null', () => {
    const hit = resolveHit();
    const session = hit.session as Json;
    const { costUsd: _cost, ...withoutCost } = session;
    const broken: [Json, string][] = [
      [{ ...hit, availability: undefined }, 'availability'],
      [{ ...hit, session: withoutCost }, 'costUsd'],
      [{ ...hit, session: { ...session, costUsd: '1.37' } }, 'costUsd'],
      [{ ...hit, session: { ...session, sessionRef: 'ses_NOT-HEX' } }, 'sessionRef'],
      [{ ...hit, coverage: { ...(hit.coverage as Json), omissions: [1] } }, 'omissions'],
      [{ ...hit, freshness: { ...(hit.freshness as Json), state: 'warm' } }, 'state'],
      [{ ...hit, coverage: { ...(hit.coverage as Json), matchedSessionCount: 1.5 } }, 'matched'],
      [
        { ...hit, freshness: { ...(hit.freshness as Json), staleAt: '2026-12-31T23:59:60Z' } },
        'staleAt',
      ],
    ];
    for (const [document, what] of broken)
      assert.notDeepEqual(issues(validateSession(document)), [], what);
  });

  test('holds a native id to the pattern Seorak publishes, before it is sent', () => {
    for (const id of [
      EXAMPLE.nativeSessionId,
      '0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47',
      'a',
      'x.y_z:1-2',
    ])
      assert.equal(NATIVE_SESSION_ID_PATTERN.test(id), true, id);
    for (const id of ['', '_x', 'a b', 'a/b', 'x'.repeat(257), 'a\n'])
      assert.equal(NATIVE_SESSION_ID_PATTERN.test(id), false, id);
  });

  test('recognizes an integration credential by its prefix and bearer syntax only', () => {
    for (const token of ['srkx_abc', 'srkx_A-b_c.d~e+f/g==', `srkx_${'f'.repeat(64)}`])
      assert.equal(CREDENTIAL_PATTERN.test(token), true, token);
    for (const token of ['srkx_', 'srkx_a b', 'srkx_a\n', 'srmcp_abc', 'abc', ' srkx_abc'])
      assert.equal(CREDENTIAL_PATTERN.test(token), false, token);
  });
});

describe("Seorak's tool-mix and cadence lenses", () => {
  const ref = EXAMPLE.sessionRef;
  const toolMix = (rows: Json[]) => readToolMixLens(otherLensDocument('tool-mix', ref, rows));
  const cadence = (rows: Json[]) => readCadenceLens(otherLensDocument('cadence', ref, rows));

  test('reads each tool with its calls, errors, cost and share, in the order Seorak gives', () => {
    const read = toolMix([
      toolMixRow('Bash', 2, 0, 0.5, 2 / 3),
      toolMixRow('Unknown tool', 1, null, null, 1 / 3),
    ]);
    assert.ok(read.ok);
    assert.deepEqual(read.value.result?.rows, [
      { label: 'Bash', calls: 2, errors: 0, costUsd: 0.5, share: 2 / 3 },
      { label: 'Unknown tool', calls: 1, errors: null, costUsd: null, share: 1 / 3 },
    ]);
  });

  test('refuses a tool mix that contradicts itself rather than guessing', () => {
    assert.deepEqual(issues(toolMix([toolMixRow('Bash', 1, 2, null, 1)])), [
      '/result/rows/0 has more errored calls than calls',
    ]);
    assert.deepEqual(issues(toolMix([toolMixRow('Bash', 1, 0, null, 1.5)])), [
      '/result/rows/0/share must be a fraction from 0 to 1',
    ]);
    assert.deepEqual(issues(toolMix([toolMixRow('Bash', 1, 0, -1, 1)])), [
      '/result/rows/0/metrics/2/value must be an amount of at least 0 or null',
    ]);
    assert.deepEqual(issues(toolMix([{ label: 'Bash', metrics: [], share: 1 }])).length, 3);
    assert.equal(readToolMixLens(otherLensDocument('cadence', ref)).ok, false);
  });

  test('reads a metric in a unit added later as unknown, never rescaled', () => {
    const row = toolMixRow('Bash', 1, 0, 0.2, 1);
    (row.metrics as Json[])[2] = {
      key: 'costUsd',
      label: 'Measured cost',
      value: 20,
      unit: 'cents',
    };
    const read = toolMix([row]);
    assert.ok(read.ok);
    assert.equal(read.value.result?.rows[0]?.costUsd, null);
  });

  test('reads the one cadence row in milliseconds, and nulls with a single moment', () => {
    const read = cadence([cadenceRow(1, null, null)]);
    assert.ok(read.ok);
    assert.deepEqual(read.value.result?.rows, [
      { label: 'Observed cadence', moments: 1, averageGap: null, longestGap: null },
    ]);
    const empty = cadence([]);
    assert.ok(empty.ok);
    assert.equal(empty.value.result?.emptyReason, 'No cadence moments were captured.');
  });

  test('refuses a cadence of two rows or an average past its longest gap', () => {
    assert.deepEqual(issues(cadence([cadenceRow(3, 1, 2), cadenceRow(3, 1, 2)])), [
      '/result/rows must hold at most one row',
    ]);
    assert.deepEqual(issues(cadence([cadenceRow(3, 5, 2)])), [
      '/result/rows/0 has an average gap longer than its longest gap',
    ]);
    const seconds = cadenceRow(3, 1, 2);
    (seconds.metrics as Json[])[2] = {
      key: 'longestGap',
      label: 'Longest gap',
      value: 2,
      unit: 'seconds',
    };
    assert.deepEqual(issues(cadence([seconds])), [
      '/result/rows/0/metrics/2/unit must be milliseconds for longestGap',
    ]);
  });
});
