import assert from 'node:assert/strict';
import { after, before, beforeEach, describe, test } from 'node:test';
import { SeorakClient } from './client.ts';
import { FakeSeorak, usageLimitsDocument, usageReading } from './testing/fake-seorak.ts';

let fake: FakeSeorak;
let client: SeorakClient;
before(async () => {
  fake = await FakeSeorak.start();
});
beforeEach(() => {
  fake.scopes = new Set(['sessions:read', 'replay:read', 'limits:read']);
  fake.usageLimits = usageLimitsDocument();
  client = new SeorakClient({ port: fake.port });
});
after(async () => fake.close());

const read = () => client.usageLimits({ credential: fake.token });

describe('provider usage limits', () => {
  test('reads each window as reported, with an unidentified account and no remaining share', async () => {
    const weekly = usageReading();
    const fiveHour = usageReading({ window: 'rolling-5h', usedPercent: 40, freshness: 'stale' });
    fake.usageLimits = usageLimitsDocument([fiveHour, weekly]);
    const result = await read();
    assert.deepEqual(result, {
      availability: 'available',
      source: { system: 'seorak', synthetic: false, api_version: 'v1' },
      readings: [
        {
          agent: 'codex', label: 'Codex', window: 'rolling-5h', used_percent: 40,
          resets_at: fiveHour.resetsAt, observed_at: fiveHour.observedAt, freshness: 'stale',
          account: { state: 'unidentified' },
        },
        {
          agent: 'codex', label: 'Codex', window: 'weekly', used_percent: 62,
          resets_at: weekly.resetsAt, observed_at: weekly.observedAt, freshness: 'fresh',
          account: { state: 'unidentified' },
        },
      ],
    });
    const request = fake.requests.at(-1);
    assert.equal(request?.method, 'GET');
    assert.equal(request?.path, '/api/v1/usage-limits');
    assert.equal(request?.headers.authorization, `Bearer ${fake.token}`);
  });

  test('keeps an agent it does not know under its own id, and never attributes an account', async () => {
    fake.usageLimits = usageLimitsDocument([
      usageReading({ agent: 'future-agent', account: { state: 'identified', ref: 'acct_1' } }),
    ]);
    const result = await read();
    assert.equal(result.availability, 'available');
    if (result.availability !== 'available') return;
    assert.equal(result.readings[0]?.label, 'future-agent');
    assert.deepEqual(result.readings[0]?.account, { state: 'unidentified' });
  });

  test('drops readings past their reset, and says so when none is left', async () => {
    fake.usageLimits = usageLimitsDocument([
      usageReading({ resetsAt: new Date(Date.now() - 1000).toISOString() }),
    ]);
    const result = await read();
    assert.equal(result.availability, 'unavailable');
    if (result.availability === 'available') return;
    assert.equal(result.reason.code, 'no_current_reading');
  });

  test('drops readings it cannot phrase: another window, source or freshness, or an unsafe id', async () => {
    fake.usageLimits = usageLimitsDocument([
      usageReading({ window: 'monthly' }),
      usageReading({ source: 'estimated' }),
      usageReading({ freshness: 'revalidating' }),
      usageReading({ agent: 'bad agent‮' }),
      usageReading({ window: 'rolling-5h' }),
    ]);
    const result = await read();
    assert.equal(result.availability, 'available');
    if (result.availability !== 'available') return;
    assert.deepEqual(result.readings.map((reading) => reading.window), ['rolling-5h']);
  });

  test('an unavailable answer is never a 0% reading', async () => {
    fake.usageLimits = usageLimitsDocument([], 'not-captured');
    assert.deepEqual(await read(), {
      availability: 'unavailable',
      reason: { code: 'not_captured', message: 'Seorak has not captured a provider limit.' },
    });
    fake.usageLimits = usageLimitsDocument([]);
    const empty = await read();
    assert.equal(empty.availability, 'unavailable');
  });

  test('a restricted credential is a setup problem, not an empty reading', async () => {
    fake.usageLimits = usageLimitsDocument([], 'outside-credential-restriction');
    const result = await read();
    assert.equal(result.availability, 'unauthorized');
    if (result.availability === 'available') return;
    assert.equal(result.reason.code, 'outside_credential_restriction');
  });

  test('a credential without limits:read gets insufficient_scope, as today\'s credential does', async () => {
    fake.scopes = new Set(['sessions:read', 'replay:read']);
    const result = await read();
    assert.equal(result.availability, 'unauthorized');
    if (result.availability === 'available') return;
    assert.equal(result.reason.code, 'insufficient_scope');
    assert.match(result.reason.message, /limits:read/);
  });

  test('refuses a document outside the format, and readings in an unavailable answer', async () => {
    fake.usageLimits = usageLimitsDocument([usageReading({ usedPercent: 101 })]);
    assert.equal((await read()).availability, 'incompatible');
    fake.usageLimits = { ...usageLimitsDocument([], 'not-captured'), readings: [usageReading()] };
    assert.equal((await read()).availability, 'incompatible');
  });

  test('sends nothing without a credential, and costs one request of the budget', async () => {
    const count = fake.requests.length;
    const absent = await client.usageLimits({ credential: null });
    assert.equal(absent.availability, 'unauthorized');
    assert.equal(fake.requests.length, count);
    const tight = new SeorakClient({ port: fake.port, requestsPerMinute: 3 });
    for (let i = 0; i < 3; i++)
      assert.equal((await tight.usageLimits({ credential: fake.token })).availability, 'available');
    const limited = await tight.usageLimits({ credential: fake.token });
    assert.equal(limited.availability, 'unavailable');
    if (limited.availability === 'available') return;
    assert.equal(limited.reason.code, 'rate_limited');
  });
});
