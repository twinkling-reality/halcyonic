import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, UnderstandingResult } from '@halcyonic/contracts';
import { SalidiumClient, salidiumProviderFor, type UnderstandOptions } from './client.ts';
import { defaultSalidiumHome } from './connection.ts';
import { consumerToken, contractError, FakeSalidium, fixture } from './testing/fake-salidium.ts';

const validateResult = compileValidator(UnderstandingResult);

const VERIFIED = { kind: 'claude-agent', id: '6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11' };
const FAILING = { kind: 'codex', id: '0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47' };
const WORKING = { kind: 'claude-agent', id: 'b27e5d10-8c4f-4a63-9e1d-3f5a7c9b2e84' };
const VERIFIED_SESSION = `claude-code:${VERIFIED.id}`;
const FAILING_SESSION = `codex:${FAILING.id}`;
const VERIFIED_REPORT = `/consumer/v1/sessions/${encodeURIComponent(VERIFIED_SESSION)}/report`;

async function start(t: TestContext): Promise<FakeSalidium> {
  const fake = await FakeSalidium.start();
  t.after(() => fake.close());
  return fake;
}

async function understand(
  fake: FakeSalidium,
  session: { kind: string; id: string },
  options: UnderstandOptions & {
    credential?: string | null;
    timeoutMs?: number;
    budgetMs?: number;
  } = {},
): Promise<UnderstandingResult> {
  const client = new SalidiumClient({
    home: fake.home,
    credential: options.credential === undefined ? fake.token : options.credential,
    timeoutMs: options.timeoutMs ?? 2_000,
    ...(options.budgetMs !== undefined && { budgetMs: options.budgetMs }),
  });
  const result = await client.understand(session.kind, session.id, options);
  const checked = validateResult(result);
  assert.ok(checked.ok, JSON.stringify(checked));
  return result;
}

function reason(result: UnderstandingResult): [string, string] {
  assert.notEqual(result.availability, 'available');
  return 'reason' in result ? [result.availability, result.reason.code] : ['', ''];
}

async function closedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return port;
}

describe('reading an understanding from Salidium', () => {
  test('checks the instance without a credential, then looks the session up and maps its report', async (t) => {
    const fake = await start(t);
    const result = await understand(fake, VERIFIED);
    assert.equal(result.availability, 'available');
    if (result.availability !== 'available') return;
    assert.equal(result.understanding.verdict.headline, '4 files changed, unverified');
    assert.equal(result.understanding.source.instance_id, fake.instanceId);
    assert.equal(result.understanding.source.evidence_sequence, 22);

    assert.deepEqual(
      fake.requests.map((request) => [request.path, request.headers.authorization]),
      [
        ['/consumer/v1/discovery', undefined],
        ['/consumer/v1/sessions/lookup', `Bearer ${fake.token}`],
        [VERIFIED_REPORT, `Bearer ${fake.token}`],
      ],
    );
    for (const { headers } of fake.requests) {
      assert.equal(headers.host, `127.0.0.1:${fake.port}`);
      assert.equal(headers.origin, undefined);
      assert.equal(headers['sec-fetch-site'], undefined);
    }
  });

  test('maps Halcyonic runtime kinds to Salidium provider ids explicitly', () => {
    assert.equal(salidiumProviderFor('claude-agent'), 'claude-code');
    assert.equal(salidiumProviderFor('codex'), 'codex');
    assert.equal(salidiumProviderFor('opencode'), 'salidium/opencode');
    for (const kind of ['claude-code', 'mock', 'OpenCode', 'Codex', 'toString', 'constructor', ''])
      assert.equal(salidiumProviderFor(kind), null, kind);
  });

  test('looks up by provider and native id, and skips the lookup when the session id is known', async (t) => {
    const fake = await start(t);
    assert.equal((await understand(fake, VERIFIED)).availability, 'available');
    const lookup = fake.requests.find((request) => request.path.endsWith('/lookup'));
    assert.deepEqual(Object.fromEntries(lookup?.query ?? []), {
      provider: 'claude-code',
      sessionId: VERIFIED.id,
    });

    const hinted = await start(t);
    const result = await understand(hinted, FAILING, { sessionId: FAILING_SESSION });
    assert.equal(result.availability, 'available');
    assert.equal(
      hinted.requests.some((request) => request.path.endsWith('/lookup')),
      false,
    );
  });

  test('reads a session that is still working, in Salidium words', async (t) => {
    const fake = await start(t);
    const result = await understand(fake, WORKING);
    assert.equal(
      result.availability === 'available' && result.understanding.verdict.headline,
      'Running a command',
    );
  });

  test('says not_found when Salidium has not observed the session', async (t) => {
    const fake = await start(t);
    const result = await understand(fake, { kind: 'codex', id: 'launched-a-moment-ago' });
    assert.deepEqual(reason(result), ['not_found', 'not_observed']);
  });

  test('says not_found when the report is gone', async (t) => {
    const fake = await start(t);
    fake.reports.delete(FAILING_SESSION);
    const result = await understand(fake, FAILING, { sessionId: FAILING_SESSION });
    assert.deepEqual(reason(result), ['not_found', 'not_observed']);
  });

  test('says not_found without a request for an id the contract cannot name', async (t) => {
    const fake = await start(t);
    for (const id of ['line\nbreak', 'x'.repeat(513)])
      assert.deepEqual(reason(await understand(fake, { kind: 'codex', id })), [
        'not_found',
        'not_observable',
      ]);
    assert.deepEqual(fake.requests, []);
  });

  test('says unavailable without a request for a runtime Salidium does not observe', async (t) => {
    const fake = await start(t);
    for (const kind of ['mock', 'claude-code']) {
      const result = await understand(fake, { kind, id: 'session-1' });
      assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    }
    assert.deepEqual(fake.requests, []);
  });

  test('asks about OpenCode only when the running daemon lists it, never on a guess', async (t) => {
    const OPENCODE = { kind: 'opencode', id: 'ses_6a1f0c2e9b7d4e3fa5c8b1d2e4f6a8c0' };
    const read: string[] = [];
    const reading = (fake: FakeSalidium) => () => {
      read.push('credential');
      return fake.token;
    };
    const ask = (fake: FakeSalidium) =>
      new SalidiumClient({
        home: fake.home,
        credential: reading(fake),
        timeoutMs: 2_000,
      }).understand(OPENCODE.kind, OPENCODE.id);

    // A daemon of contract 1.0 says nothing about providers, so OpenCode stays unobserved.
    const older = await start(t);
    older.providers = ['claude-code', 'codex', 'salidium/opencode'];
    older.writeDiscovery();
    assert.deepEqual(reason(await ask(older)), ['unavailable', 'runtime_not_observed']);
    assert.deepEqual(older.authorizedRequests(), [], 'a list beside minor 0 is not read');

    // Contract 1.1 without OpenCode in the list.
    const without = await start(t);
    without.minor = 1;
    without.providers = ['claude-code', 'codex'];
    without.writeDiscovery();
    assert.deepEqual(reason(await ask(without)), ['unavailable', 'runtime_not_observed']);
    assert.deepEqual(without.authorizedRequests(), []);

    // Not running: what it would observe is unknown, so the answer is what it always was.
    const stopped = await start(t);
    stopped.removeDiscovery();
    assert.deepEqual(reason(await ask(stopped)), ['unavailable', 'runtime_not_observed']);
    assert.deepEqual(read, [], 'the credential is not read for a session no daemon observes');

    // Contract 1.1 listing it: looked up by Salidium's provider id and OpenCode's own session id.
    const listing = await start(t);
    listing.minor = 1;
    listing.providers = ['claude-code', 'codex', 'salidium/opencode'];
    listing.writeDiscovery();
    const report = structuredClone(fixture('session-report-verified'));
    const session = report.session as Record<string, unknown>;
    session.id = `salidium/opencode:${OPENCODE.id}`;
    session.native = { provider: 'salidium/opencode', sessionId: OPENCODE.id };
    listing.reports.set(session.id as string, report);
    const result = await ask(listing);
    assert.equal(result.availability, 'available');
    const lookup = listing.requests.find((request) => request.path.endsWith('/lookup'));
    assert.deepEqual(Object.fromEntries(lookup?.query ?? []), {
      provider: 'salidium/opencode',
      sessionId: OPENCODE.id,
    });
    assert.deepEqual(read, ['credential'], 'read once a request carries it');
  });

  test('stops asking about a provider a daemon that lists its providers leaves out', async (t) => {
    const fake = await start(t);
    fake.minor = 1;
    fake.providers = ['claude-code'];
    fake.writeDiscovery();
    const result = await understand(fake, FAILING);
    assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    assert.match(
      'reason' in result ? result.reason.message : '',
      /not observing sessions of the codex runtime now/,
    );
    assert.deepEqual(fake.authorizedRequests(), []);
    assert.equal((await understand(fake, VERIFIED)).availability, 'available');
  });

  test('refuses a providers list that breaks the contract', async (t) => {
    for (const providers of [[{ id: 'OpenCode' }], 'claude-code', [{}]]) {
      const fake = await start(t);
      fake.minor = 1;
      fake.overrides.set('/consumer/v1/discovery', (response) => {
        response.setHeader('Content-Type', 'application/json');
        response.end(JSON.stringify({ ...fake.discovery(), providers }));
      });
      fake.writeDiscovery({ ...fake.discovery(), providers });
      assert.deepEqual(reason(await understand(fake, VERIFIED)), [
        'incompatible',
        'invalid_document',
      ]);
      assert.deepEqual(fake.authorizedRequests(), []);
    }
  });

  test('says unavailable when Salidium is not running', async (t) => {
    const fake = await start(t);
    fake.removeDiscovery();
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'not_running']);
    assert.deepEqual(fake.requests, []);
  });

  test('says unavailable when the discovery file names a port nothing answers on', async (t) => {
    const fake = await start(t);
    const baseUrl = `http://127.0.0.1:${await closedPort()}/consumer/v1`;
    fake.writeDiscovery({ ...fake.discovery(), contracts: [{ ...fake.contract(), baseUrl }] });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'unreachable']);
  });

  test('uses the major 1 entry and ignores the others listed beside it', async (t) => {
    const fake = await start(t);
    const v2 = {
      ...fake.contract(),
      major: 2,
      baseUrl: `http://127.0.0.1:${await closedPort()}/consumer/v2`,
    };
    fake.writeDiscovery({ ...fake.discovery(), contracts: [v2, fake.contract()] });
    const result = await understand(fake, VERIFIED);
    assert.equal(result.availability, 'available');
    assert.deepEqual(result.availability === 'available' && result.understanding.source.contract, {
      name: 'salidium.consumer',
      major: 1,
      minor: 0,
    });
  });

  test('never sends the credential to a port that does not prove it is the same instance', async (t) => {
    const fake = await start(t);
    fake.writeDiscovery({ ...fake.discovery(), instanceId: 'f'.repeat(32) });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'unavailable',
      'instance_mismatch',
    ]);

    fake.writeDiscovery();
    fake.overrides.set('/consumer/v1/discovery', (response) => {
      response.statusCode = 404;
      response.end('not found');
    });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'unavailable',
      'instance_mismatch',
    ]);
    assert.deepEqual(fake.authorizedRequests(), []);
  });

  test("says unavailable when Salidium's loopback guard refuses the request", async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/discovery', (response) =>
      contractError(response, 421, 'host-not-allowed', 'only a loopback Host is accepted'),
    );
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'host_not_allowed']);
    assert.deepEqual(fake.authorizedRequests(), []);

    fake.overrides.clear();
    fake.overrides.set(VERIFIED_REPORT, (response) =>
      contractError(response, 403, 'origin-not-allowed', 'cross-origin requests are refused'),
    );
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'unavailable',
      'origin_not_allowed',
    ]);
  });

  test('says unavailable when Salidium fails to answer', async (t) => {
    const fake = await start(t);
    fake.overrides.set(VERIFIED_REPORT, (response) =>
      contractError(response, 500, 'internal', 'the request could not be completed'),
    );
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'server_error']);
  });

  test('says incompatible when Salidium does not offer major version 1', async (t) => {
    const fake = await start(t);
    const v2 = { ...fake.contract(), major: 2, baseUrl: 'http://127.0.0.1:1/consumer/v2' };
    fake.writeDiscovery({ ...fake.discovery(), contracts: [v2] });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'incompatible',
      'unsupported_contract',
    ]);
    assert.deepEqual(fake.requests, []);
  });

  test('says incompatible when a document does not match the contract', async (t) => {
    const fake = await start(t);
    fake.writeDiscovery('{"format": "salidium.consumer-discovery", "version": 1, ');
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'incompatible',
      'invalid_document',
    ]);

    fake.writeDiscovery();
    const { verdict: _verdict, ...withoutVerdict } = fixture('session-report-verified');
    fake.reports.set(VERIFIED_SESSION, withoutVerdict);
    const result = await understand(fake, VERIFIED);
    assert.deepEqual(reason(result), ['incompatible', 'invalid_document']);
    assert.match(
      result.availability === 'incompatible' ? result.reason.message : '',
      /required properties verdict/,
    );
  });

  test('refuses a report that names a different session than the one requested', async (t) => {
    const fake = await start(t);
    const result = await understand(fake, VERIFIED, { sessionId: FAILING_SESSION });
    assert.deepEqual(reason(result), ['incompatible', 'invalid_document']);
  });

  test('says incompatible for an answer the contract does not give', async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/sessions/lookup', (response) =>
      contractError(response, 404, 'not-found', 'no such consumer endpoint'),
    );
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'incompatible',
      'unexpected_status',
    ]);
    fake.overrides.set('/consumer/v1/sessions/lookup', (response) => {
      response.statusCode = 418;
      response.end();
    });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), [
      'incompatible',
      'unexpected_status',
    ]);
  });

  test('says unauthorized without sending a missing or malformed credential', async (t) => {
    const fake = await start(t);
    assert.deepEqual(reason(await understand(fake, VERIFIED, { credential: null })), [
      'unauthorized',
      'credential_missing',
    ]);
    for (const credential of ['nope', `${fake.token}\n`, fake.token.toUpperCase()])
      assert.deepEqual(reason(await understand(fake, VERIFIED, { credential })), [
        'unauthorized',
        'credential_malformed',
      ]);
    assert.deepEqual(fake.authorizedRequests(), []);
  });

  test('says unauthorized when Salidium refuses the credential', async (t) => {
    const fake = await start(t);
    const result = await understand(fake, VERIFIED, { credential: consumerToken() });
    assert.deepEqual(reason(result), ['unauthorized', 'credential_rejected']);
  });

  test('gives up on a request Salidium does not answer in time, and says so apart from no answer', async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/discovery', () => {});
    const result = await understand(fake, VERIFIED, { timeoutMs: 100 });
    assert.deepEqual(reason(result), ['unavailable', 'timed_out']);
    const message = 'reason' in result ? result.reason.message : '';
    assert.equal(
      message,
      'No answer in time: it may still be catching up after an update. Press Refresh in a moment.',
    );
    assert.doesNotMatch(message, /Salidium/, 'the provenance line names the source');
  });

  test('waits several seconds for a late answer, within one budget for the whole read', async (t) => {
    const fake = await start(t);
    const report = fake.reports.get(VERIFIED_SESSION);
    // A report that comes late, as one replayed on Salidium's main thread after an upgrade.
    fake.overrides.set(VERIFIED_REPORT, (response) => {
      setTimeout(() => {
        response.setHeader('Content-Type', 'application/json');
        response.end(JSON.stringify(report));
      }, 300);
    });
    assert.equal(
      (await understand(fake, VERIFIED, { timeoutMs: 1_000 })).availability,
      'available',
    );

    const begun = Date.now();
    const late = await understand(fake, VERIFIED, { timeoutMs: 1_000, budgetMs: 150 });
    assert.deepEqual(reason(late), ['unavailable', 'timed_out']);
    assert.ok(
      Date.now() - begun < 900,
      'the whole read ends with its budget, not the request timeout',
    );
  });

  test('rejects when the caller aborts, which is not a state of Salidium', async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/discovery', () => {});
    const controller = new AbortController();
    const pending = understand(fake, VERIFIED, { signal: controller.signal });
    setTimeout(() => controller.abort(), 20);
    await assert.rejects(pending, { name: 'AbortError' });
  });

  test('ignores what a later minor version adds', async (t) => {
    const fake = await start(t);
    const contract = { ...fake.contract(), addedLater: [] };
    fake.writeDiscovery({ ...fake.discovery(), contracts: [contract], addedLater: { x: 1 } });
    fake.reports.set(VERIFIED_SESSION, { ...fixture('session-report-verified'), addedLater: 1 });
    assert.equal((await understand(fake, VERIFIED)).availability, 'available');
  });

  test('finds Salidium where Salidium keeps its state', () => {
    assert.equal(defaultSalidiumHome({}, '/home/dev'), '/home/dev/.salidium');
    assert.equal(
      defaultSalidiumHome({ SALIDIUM_HOME: '/srv/salidium' }, '/home/dev'),
      '/srv/salidium',
    );
  });
});
