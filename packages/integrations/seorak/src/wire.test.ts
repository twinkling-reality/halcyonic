import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
  EXAMPLE,
  lensDocument,
  outcomeDocument,
  resolveHit,
  resolveMiss,
} from './testing/fake-seorak.ts';
import {
  CREDENTIAL_PATTERN,
  NATIVE_SESSION_ID_PATTERN,
  readLens,
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

  test('accepts outcome and lens documents built from the published types', () => {
    assert.equal(validateOutcome(outcomeDocument(EXAMPLE.sessionRef)).ok, true);
    const lens = readLens(lensDocument(EXAMPLE.sessionRef));
    assert.ok(lens.ok);
    assert.deepEqual(lens.value.result?.rows, [
      { label: 'test', runs: 5, passed: 4, passRate: 0.8 },
      { label: 'typecheck', runs: 2, passed: 2, passRate: 1 },
    ]);
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
      [{ ...hit, coverage: { ...(hit.coverage as Json), omissions: ['forgotten'] } }, 'omissions'],
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
