import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { isFailure, readDiscovery } from './connection.ts';
import { fixture } from './testing/fake-salidium.ts';
import { readFeedMessage, validateError, validateLookup, validateReport } from './wire.ts';

type Json = Record<string, unknown>;

const FEED_TYPES = ['resync', 'session-changed', 'session-removed', 'heartbeat', 'closing'];
const REPORTS = ['session-report-verified', 'session-report-failing', 'session-report-working'];
const MAJOR_ONE = { name: 'salidium.consumer', major: 1, minor: 0 };

/** Adds a property at the root and at several depths, as a later minor version may. */
function extended(report: Json): Json {
  const added = (value: unknown) =>
    value === null ? null : { ...(value as Json), addedLater: [1] };
  const changes = report.changes as { files: Json[] };
  const verification = report.verification as { latestByMethod: Json[] };
  const explanation = report.explanation as { content: Json | null };
  return {
    ...report,
    addedLater: { any: 'shape' },
    session: added(report.session),
    verdict: added(report.verdict),
    waiting: added(report.waiting),
    changes: { ...changes, files: changes.files.map(added) },
    verification: { ...verification, latestByMethod: verification.latestByMethod.map(added) },
    explanation: { ...explanation, content: added(explanation.content) },
  };
}

function discovery(contracts: unknown[]): Json {
  return { ...fixture('consumer-discovery'), contracts };
}

function discovered(document: unknown) {
  const result = readDiscovery(document, 'discovery file');
  return isFailure(result) ? `${result.availability}/${result.reason.code}` : result;
}

describe('consumer contract v1 reader', () => {
  test('accepts every retained fixture this client reads', () => {
    const found = discovered(fixture('consumer-discovery'));
    if (typeof found === 'string') assert.fail(found);
    assert.deepEqual(found.contract, {
      ...MAJOR_ONE,
      baseUrl: 'http://127.0.0.1:47822/consumer/v1',
    });
    assert.equal(validateError(fixture('consumer-error-session-not-observed')).ok, true);
    assert.equal(validateError(fixture('consumer-error-unauthorized')).ok, true);
    assert.equal(validateLookup(fixture('session-lookup')).ok, true);
    for (const name of REPORTS) assert.equal(validateReport(fixture(name)).ok, true, name);
    for (const type of FEED_TYPES)
      assert.equal(readFeedMessage(fixture(`session-feed-${type}`))?.ok, true, type);
  });

  test('takes the major 1 entry and ignores every other, so a later major never hides it', () => {
    const v1 = { ...MAJOR_ONE, baseUrl: 'http://127.0.0.1:47822/consumer/v1' };
    const v2 = { ...MAJOR_ONE, major: 2, baseUrl: 'http://127.0.0.1:47822/consumer/v2' };
    const foreign = { name: 'salidium.sync', major: 1, anything: { goes: true } };
    for (const contracts of [
      [v1, v2],
      [v2, v1],
      [foreign, 'not an entry', null, v1],
    ]) {
      const found = discovered(discovery(contracts));
      if (typeof found === 'string') assert.fail(found);
      assert.deepEqual(found.contract, v1);
    }
  });

  test('says unsupported when no entry is major 1 of salidium.consumer', () => {
    const v2 = { ...MAJOR_ONE, major: 2, baseUrl: 'http://127.0.0.1:47822/consumer/v2' };
    assert.equal(discovered(discovery([v2])), 'incompatible/unsupported_contract');
    assert.equal(
      discovered(discovery([{ ...v2, name: 'other', major: 1 }])),
      'incompatible/unsupported_contract',
    );
    assert.equal(discovered(discovery([])), 'incompatible/invalid_document');
  });

  test('holds the major 1 base URL to loopback, because it decides where the credential goes', () => {
    for (const baseUrl of [
      'http://evil.example:47822/consumer/v1',
      'http://127.0.0.1.evil.example:47822/consumer/v1',
      'http://127.0.0.1@evil.example:47822/consumer/v1',
      'https://127.0.0.1:47822/consumer/v1',
      'http://localhost:47822/consumer/v1',
      'http://127.0.0.1:47822/consumer/v2',
    ])
      assert.equal(
        discovered(discovery([{ ...MAJOR_ONE, baseUrl }])),
        'incompatible/invalid_document',
        baseUrl,
      );
    const instanceId = 'not-hex';
    const odd = { ...fixture('consumer-discovery'), instanceId };
    assert.equal(discovered(odd), 'incompatible/invalid_document');
  });

  test('ignores properties a later minor version may add', () => {
    for (const name of REPORTS)
      assert.equal(validateReport(extended(fixture(name))).ok, true, name);
    const entry = { ...MAJOR_ONE, baseUrl: 'http://127.0.0.1:47822/consumer/v1', addedLater: 1 };
    assert.equal(typeof discovered({ ...discovery([entry]), addedLater: true }), 'object');
    const changed = { ...fixture('session-feed-session-changed'), addedLater: { nested: true } };
    assert.equal(readFeedMessage(changed)?.ok, true);
  });

  test('rejects a missing property, a wrong type, an unknown enumeration value and an omitted null', () => {
    const report = fixture('session-report-failing');
    const { verdict: _verdict, ...withoutVerdict } = report;
    const { waiting: _waiting, ...withoutWaiting } = report;
    const session = report.session as Json;
    const verification = report.verification as Json;
    const [run] = verification.latestByMethod as Json[];
    const { runner: _runner, ...runWithoutRunner } = run as Json;
    const waiting = report.waiting as Json;
    const { provenance: _provenance, ...waitingWithoutClass } = waiting;
    const broken: Json[] = [
      withoutVerdict,
      withoutWaiting,
      { ...report, version: 1 },
      { ...report, session: { ...session, evidenceSeq: '7' } },
      { ...report, verdict: { ...(report.verdict as Json), tone: 'maybe' } },
      { ...report, waiting: { ...waiting, kind: 'approval' } },
      { ...report, waiting: waitingWithoutClass },
      { ...report, verification: { ...verification, latestByMethod: [runWithoutRunner] } },
      { ...report, generatedAt: '2026-09-20T16:20:00Z' },
    ];
    for (const document of broken) assert.equal(validateReport(document).ok, false);
  });

  test('refuses a statement the user wrote, which the contract no longer carries', () => {
    const report = fixture('session-report-verified');
    const latest = report.latestStatement as Json;
    assert.equal(
      validateReport({ ...report, latestStatement: { ...latest, author: 'user' } }).ok,
      false,
    );
    assert.equal(
      validateReport({ ...report, latestStatement: { ...latest, author: null } }).ok,
      true,
    );
  });

  test('enforces the maximum lengths the contract publishes', () => {
    const report = fixture('session-report-working');
    const verdict = report.verdict as Json;
    assert.equal(
      validateReport({ ...report, verdict: { ...verdict, headline: 'x'.repeat(300) } }).ok,
      true,
    );
    assert.equal(
      validateReport({ ...report, verdict: { ...verdict, headline: 'x'.repeat(301) } }).ok,
      false,
    );
  });

  test('knows the guard refusals and the internal failure as contract errors', () => {
    for (const error of ['host-not-allowed', 'origin-not-allowed', 'internal'])
      assert.equal(
        validateError({ ...fixture('consumer-error-unauthorized'), error }).ok,
        true,
        error,
      );
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
