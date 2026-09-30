import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, afterEach, before, describe, test } from 'node:test';
import { compileValidator, UsageLimitsResponse } from '@halcyonic/contracts';
import {
  FakeSeorak,
  usageLimitsDocument,
  usageReading,
} from '@halcyonic/integration-seorak/testing';
import { seorakEvaluation } from '../intelligence/evaluation.ts';
import { startTestServer } from '../testing/harness.ts';

const validate = compileValidator(UsageLimitsResponse);

/** The control plane reading a stub Seorak plane over loopback, with a credential file of mode 600. */
describe('usage limits read through a stub Seorak', () => {
  let seorak: FakeSeorak;
  let server: Awaited<ReturnType<typeof startTestServer>>;
  before(async () => {
    seorak = await FakeSeorak.start();
    const dir = mkdtempSync(join(tmpdir(), 'halcyonic-usage-'));
    const credentialPath = join(dir, 'seorak-credential');
    writeFileSync(credentialPath, `${seorak.token}\n`);
    chmodSync(credentialPath, 0o600);
    server = await startTestServer({
      evaluation: seorakEvaluation({ credentialPath, port: seorak.port }),
    });
  });
  afterEach(() => {
    seorak.scopes = new Set(['sessions:read', 'replay:read']);
    seorak.usageLimits = usageLimitsDocument();
  });
  after(async () => {
    await server.stop();
    await seorak.close();
  });

  const get = (headers: Record<string, string> = { authorization: `Bearer ${server.token}` }) =>
    fetch(`${server.baseUrl}/api/usage-limits`, { headers });

  test('needs the access token, reads once per request and journals nothing', async () => {
    seorak.scopes.add('limits:read');
    const reading = usageReading();
    seorak.usageLimits = usageLimitsDocument([reading]);
    const before = seorak.requests.length;
    assert.equal((await get({})).status, 401);
    assert.equal(seorak.requests.length, before);
    const head = server.controlPlane.journal.head();
    const response = await get();
    assert.equal(response.status, 200);
    const body = (await response.json()) as UsageLimitsResponse;
    assert.equal(validate(body).ok, true);
    assert.deepEqual(body, {
      availability: 'available',
      source: { system: 'seorak', synthetic: false, api_version: 'v1' },
      readings: [
        {
          agent: 'codex',
          label: 'Codex',
          window: 'weekly',
          used_percent: 62,
          resets_at: reading.resetsAt,
          observed_at: reading.observedAt,
          freshness: 'fresh',
          account: { state: 'unidentified' },
        },
      ],
    });
    assert.equal(seorak.requests.length, before + 1);
    assert.equal(seorak.requests.at(-1)?.path, '/api/v1/usage-limits');
    assert.equal(server.controlPlane.journal.head(), head);
  });

  test("today's credential, without limits:read, is refused as insufficient_scope", async () => {
    const body = (await (await get()).json()) as UsageLimitsResponse;
    assert.equal(body.availability, 'unauthorized');
    assert.equal(body.reason.code, 'insufficient_scope');
  });

  test('an agent id outside the pattern is dropped, never passed through', async () => {
    seorak.scopes.add('limits:read');
    seorak.usageLimits = usageLimitsDocument([
      usageReading({ agent: 'codex\u202e<b>' }),
      usageReading({ agent: 'x'.repeat(65) }),
      usageReading({ agent: 'codex', window: 'rolling-5h' }),
    ]);
    const body = (await (await get()).json()) as UsageLimitsResponse;
    assert.equal(body.availability, 'available');
    if (body.availability !== 'available') return;
    assert.deepEqual(
      body.readings.map((reading) => [reading.agent, reading.window]),
      [['codex', 'rolling-5h']],
    );
    seorak.usageLimits = usageLimitsDocument([usageReading({ agent: '../codex' })]);
    const none = (await (await get()).json()) as UsageLimitsResponse;
    assert.equal(none.availability, 'unavailable');
  });

  test('no captured limit is unavailable, never a reading of 0%', async () => {
    seorak.scopes.add('limits:read');
    seorak.usageLimits = usageLimitsDocument([], 'not-captured');
    const body = (await (await get()).json()) as UsageLimitsResponse;
    assert.equal(body.availability, 'unavailable');
    assert.equal('readings' in body, false);
  });
});

describe('usage limits from other sources', () => {
  let answer: unknown;
  let server: Awaited<ReturnType<typeof startTestServer>>;
  before(async () => {
    server = await startTestServer({
      evaluation: {
        evaluate: async () => ({
          availability: 'unavailable',
          reason: { code: 'unused', message: 'Unused.' },
        }),
        usageLimits: async () => answer as UsageLimitsResponse,
      },
    });
  });
  after(async () => server.stop());

  test('data outside the contract is never passed through', async () => {
    answer = { availability: 'available', readings: [] };
    const response = await fetch(`${server.baseUrl}/api/usage-limits`, {
      headers: { authorization: `Bearer ${server.token}` },
    });
    const body = (await response.json()) as UsageLimitsResponse;
    assert.equal(body.availability, 'incompatible');
  });

  test('a source without usage limits says it is not configured', async () => {
    const bare = await startTestServer({
      evaluation: {
        evaluate: async () => ({
          availability: 'unavailable',
          reason: { code: 'unused', message: 'Unused.' },
        }),
      },
    });
    try {
      const response = await fetch(`${bare.baseUrl}/api/usage-limits`, {
        headers: { authorization: `Bearer ${bare.token}` },
      });
      assert.deepEqual(await response.json(), {
        availability: 'unavailable',
        reason: { code: 'not_configured', message: 'No usage limit source is configured.' },
      });
    } finally {
      await bare.stop();
    }
  });
});
