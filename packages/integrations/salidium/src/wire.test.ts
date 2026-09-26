import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { fixture } from './testing/fake-salidium.ts';
import {
  readFeedMessage,
  validateDiscovery,
  validateError,
  validateLookup,
  validateReport,
} from './wire.ts';

type Json = Record<string, unknown>;

const FEED_TYPES = ['resync', 'session-changed', 'session-removed', 'heartbeat', 'closing'];

/** Adds a property at the root and at several depths, as a later minor version may. */
function extended(report: Json): Json {
  const changes = report.changes as Json;
  const [file] = changes.files as Json[];
  const verification = report.verification as Json;
  const [run] = verification.latestByMethod as Json[];
  const explanation = report.explanation as Json;
  return {
    ...report,
    addedLater: { any: 'shape' },
    session: { ...(report.session as Json), addedLater: 1 },
    verdict: { ...(report.verdict as Json), addedLater: null },
    changes: { ...changes, files: [{ ...file, addedLater: [] }] },
    verification: { ...verification, latestByMethod: [{ ...run, addedLater: true }] },
    explanation: { ...explanation, content: { ...(explanation.content as Json), addedLater: 'x' } },
  };
}

describe('consumer contract v1 reader', () => {
  test('accepts every retained fixture this client reads', () => {
    assert.equal(validateDiscovery(fixture('consumer-discovery')).ok, true);
    assert.equal(validateError(fixture('consumer-error-session-not-observed')).ok, true);
    assert.equal(validateError(fixture('consumer-error-unauthorized')).ok, true);
    assert.equal(validateLookup(fixture('session-lookup')).ok, true);
    assert.equal(validateReport(fixture('session-report-verified')).ok, true);
    assert.equal(validateReport(fixture('session-report-failing')).ok, true);
    for (const type of FEED_TYPES)
      assert.equal(readFeedMessage(fixture(`session-feed-${type}`))?.ok, true, type);
  });

  test('ignores properties a later minor version may add', () => {
    assert.equal(validateReport(extended(fixture('session-report-verified'))).ok, true);
    assert.equal(validateDiscovery({ ...fixture('consumer-discovery'), addedLater: 1 }).ok, true);
    const changed = { ...fixture('session-feed-session-changed'), addedLater: { nested: true } };
    assert.equal(readFeedMessage(changed)?.ok, true);
  });

  test('rejects a missing property, a wrong type, an unknown enumeration value and an omitted null', () => {
    const report = fixture('session-report-failing');
    const { verdict: _verdict, ...withoutVerdict } = report;
    const session = report.session as Json;
    const verification = report.verification as Json;
    const [run] = verification.latestByMethod as Json[];
    const { runner: _runner, ...runWithoutRunner } = run as Json;
    const broken: Json[] = [
      withoutVerdict,
      { ...report, version: 1 },
      { ...report, session: { ...session, evidenceSeq: '7' } },
      { ...report, verdict: { ...(report.verdict as Json), tone: 'maybe' } },
      { ...report, verification: { ...verification, latestByMethod: [runWithoutRunner] } },
      { ...report, generatedAt: '2026-09-20T16:20:00Z' },
    ];
    for (const document of broken) assert.equal(validateReport(document).ok, false);
  });

  test('holds the discovery base URL to loopback, because it decides where the credential goes', () => {
    for (const baseUrl of [
      'http://evil.example:47822/consumer/v1',
      'http://127.0.0.1.evil.example:47822/consumer/v1',
      'http://127.0.0.1@evil.example:47822/consumer/v1',
      'https://127.0.0.1:47822/consumer/v1',
      'http://localhost:47822/consumer/v1',
      'http://127.0.0.1:47822/consumer/v2',
    ])
      assert.equal(validateDiscovery({ ...fixture('consumer-discovery'), baseUrl }).ok, false);
    const instanceId = 'not-hex';
    assert.equal(validateDiscovery({ ...fixture('consumer-discovery'), instanceId }).ok, false);
  });

  test('skips a feed message type it does not know', () => {
    const future = { format: 'salidium.session-feed', version: 1, type: 'session.renamed' };
    assert.equal(readFeedMessage(future), null);
  });

  test('rejects a malformed known feed message and anything that is not a v1 feed message', () => {
    const { evidenceSeq: _seq, ...changed } = fixture('session-feed-session-changed');
    assert.equal(readFeedMessage(changed)?.ok, false);
    assert.equal(
      readFeedMessage({ ...fixture('session-feed-closing'), reason: 'bored' })?.ok,
      false,
    );
    assert.equal(readFeedMessage({ ...fixture('session-feed-heartbeat'), version: 2 })?.ok, false);
    assert.equal(readFeedMessage({ ...fixture('session-feed-heartbeat'), format: 'x' })?.ok, false);
    assert.equal(readFeedMessage('heartbeat')?.ok, false);
  });

  test('accepts a removal whose native identity Salidium could not state', () => {
    const removed = { ...fixture('session-feed-session-removed'), native: null };
    assert.equal(readFeedMessage(removed)?.ok, true);
  });
});
