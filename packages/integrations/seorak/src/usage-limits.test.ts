import assert from 'node:assert/strict';
import { after, before, describe, test } from 'node:test';
import { SeorakClient } from './client.ts';
import { FakeSeorak } from './testing/fake-seorak.ts';

let fake: FakeSeorak;
let client: SeorakClient;
before(async () => {
  fake = await FakeSeorak.start();
  client = new SeorakClient({ port: fake.port });
});
after(async () => fake.close());

function answer(document: object) {
  fake.overrides.set('/api/v1/usage-limits', (response) => {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify(document));
  });
}

const row = {
  tool: 'codex', period: 'rolling-5h', consumed: null, unit: null,
  allowance: null, usedPercent: 31, resetsAt: '2099-09-30T20:00:00.000Z',
  observedAt: '2026-09-30T18:00:00.000Z', source: 'provider-auth', coverageComplete: true,
};
const document = {
  apiVersion: 'v1', availability: { state: 'available', reason: null },
  freshness: { state: 'fresh', generatedAt: '2026-09-30T18:00:00.000Z',
    dataThrough: '2026-09-30T18:00:00.000Z', staleAt: '2026-09-30T18:05:00.000Z' },
  readings: [row],
};

describe('account-wide usage limits', () => {
  test('reads a captured provider quota without inventing counts or a session link', async () => {
    answer(document);
    const result = await client.usageLimits({ credential: fake.token });
    assert.deepEqual(result, { availability: 'available', readings: [{
      provider: 'codex', window: 'rolling-5h', remaining_percent: 69,
      resets_at: row.resetsAt, observed_at: row.observedAt,
    }] });
    assert.equal(fake.requests.at(-1)?.path, '/api/v1/usage-limits');
    assert.equal(fake.requests.at(-1)?.method, 'GET');
  });

  test('never makes a percentage from count-only or stale readings', async () => {
    answer({ ...document, readings: [
      { ...row, tool: 'claude-code', consumed: 1000, unit: 'tokens',
        usedPercent: null, source: 'none', coverageComplete: false },
      { ...row, resetsAt: '2020-01-01T00:00:00.000Z' },
    ] });
    const result = await client.usageLimits({ credential: fake.token });
    assert.equal(result.availability, 'unavailable');
  });

  test('rejects invalid provider percentages and missing credentials', async () => {
    answer({ ...document, readings: [{ ...row, usedPercent: 101 }] });
    const invalid = await client.usageLimits({ credential: fake.token });
    assert.equal(invalid.availability, 'incompatible');
    const count = fake.requests.length;
    const absent = await client.usageLimits({ credential: null });
    assert.equal(absent.availability, 'unauthorized');
    assert.equal(fake.requests.length, count);
  });
});
