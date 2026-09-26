import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { describe, type TestContext, test } from 'node:test';
import { compileValidator, UnderstandingResult } from '@halcyonic/contracts';
import { SalidiumClient, salidiumProviderFor, type UnderstandOptions } from './client.ts';
import { defaultSalidiumHome } from './connection.ts';
import { consumerToken, FakeSalidium, fixture } from './testing/fake-salidium.ts';

const validateResult = compileValidator(UnderstandingResult);

const VERIFIED = { kind: 'claude-code', id: '6f1c2a90-3b7e-4d15-9a2c-0e8b5d7f4c11' };
const FAILING = { kind: 'codex', id: '0199a3f2-7c4e-7b10-8d2a-5e6f9c1b3a47' };
const VERIFIED_SESSION = `claude-code:${VERIFIED.id}`;
const FAILING_SESSION = `codex:${FAILING.id}`;

async function start(t: TestContext): Promise<FakeSalidium> {
  const fake = await FakeSalidium.start();
  t.after(() => fake.close());
  return fake;
}

async function understand(
  fake: FakeSalidium,
  session: { kind: string; id: string },
  options: UnderstandOptions & { credential?: string | null; timeoutMs?: number } = {},
): Promise<UnderstandingResult> {
  const client = new SalidiumClient({
    home: fake.home,
    credential: options.credential === undefined ? fake.token : options.credential,
    timeoutMs: options.timeoutMs ?? 2_000,
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
    assert.equal(result.understanding.verdict.headline, '3 files changed, unverified');
    assert.equal(result.understanding.source.instance_id, fake.instanceId);
    assert.equal(result.understanding.source.evidence_sequence, 20);

    assert.deepEqual(
      fake.requests.map((request) => [request.path, request.headers.authorization]),
      [
        ['/consumer/v1/discovery', undefined],
        ['/consumer/v1/sessions/lookup', `Bearer ${fake.token}`],
        [
          `/consumer/v1/sessions/${encodeURIComponent(VERIFIED_SESSION)}/report`,
          `Bearer ${fake.token}`,
        ],
      ],
    );
    for (const { headers } of fake.requests) {
      assert.equal(headers.host, `127.0.0.1:${fake.port}`);
      assert.equal(headers.origin, undefined);
      assert.equal(headers['sec-fetch-site'], undefined);
    }
  });

  test('maps Halcyonic runtime kinds to Salidium provider ids explicitly', () => {
    assert.equal(salidiumProviderFor('claude-code'), 'claude-code');
    assert.equal(salidiumProviderFor('codex'), 'codex');
    for (const kind of ['mock', 'opencode', 'Claude-Code', 'toString', 'constructor', ''])
      assert.equal(salidiumProviderFor(kind), null, kind);
  });

  test('looks up by provider and native id, and skips the lookup when the session id is known', async (t) => {
    const fake = await start(t);
    assert.equal((await understand(fake, FAILING)).availability, 'available');
    const lookup = fake.requests.find((request) => request.path.endsWith('/lookup'));
    assert.deepEqual(Object.fromEntries(lookup?.query ?? []), {
      provider: 'codex',
      sessionId: FAILING.id,
    });

    const hinted = await start(t);
    const result = await understand(hinted, FAILING, { sessionId: FAILING_SESSION });
    assert.equal(result.availability, 'available');
    assert.equal(
      hinted.requests.some((request) => request.path.endsWith('/lookup')),
      false,
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
    const result = await understand(fake, { kind: 'mock', id: 'mock-session-1' });
    assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    assert.deepEqual(fake.requests, []);
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
    fake.writeDiscovery({ ...fake.discovery(), baseUrl });
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'unreachable']);
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

  test('says incompatible when Salidium offers another contract version', async (t) => {
    const fake = await start(t);
    const contract = { name: 'salidium.consumer', major: 2, minor: 0 };
    fake.writeDiscovery({
      ...fake.discovery(),
      contract,
      baseUrl: 'http://127.0.0.1:1/consumer/v2',
    });
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
    fake.overrides.set('/consumer/v1/sessions/lookup', (response) => {
      response.statusCode = 404;
      response.end(
        JSON.stringify({ ...fixture('consumer-error-session-not-observed'), error: 'not-found' }),
      );
    });
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

  test('says unavailable when Salidium fails to answer', async (t) => {
    const fake = await start(t);
    fake.overrides.set(
      `/consumer/v1/sessions/${encodeURIComponent(VERIFIED_SESSION)}/report`,
      (response) => {
        response.statusCode = 500;
        response.end(JSON.stringify({ error: 'internal error' }));
      },
    );
    assert.deepEqual(reason(await understand(fake, VERIFIED)), ['unavailable', 'server_error']);
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

  test('gives up on a request Salidium does not answer in time', async (t) => {
    const fake = await start(t);
    fake.overrides.set('/consumer/v1/discovery', () => {});
    const result = await understand(fake, VERIFIED, { timeoutMs: 100 });
    assert.deepEqual(reason(result), ['unavailable', 'unreachable']);
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
    fake.writeDiscovery({ ...fake.discovery(), addedLater: { feature: true } });
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
