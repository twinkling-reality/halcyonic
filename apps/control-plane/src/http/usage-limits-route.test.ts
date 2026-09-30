import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { compileValidator, type UsageLimitsResponse, UsageLimitsResponse as Schema } from '@halcyonic/contracts';
import { startTestServer } from '../testing/harness.ts';

let answer: UsageLimitsResponse;
let calls = 0;
let server: Awaited<ReturnType<typeof startTestServer>>;
before(async () => {
  server = await startTestServer({ evaluation: {
    evaluate: async () => ({ availability: 'unavailable', reason: { code: 'unused', message: 'Unused.' } }),
    usageLimits: async () => { calls++; return answer; },
  } });
});
after(async () => server.stop());

test('usage is authenticated, read on demand and never journaled', async () => {
  answer = { availability: 'available', readings: [{ provider: 'codex', window: 'weekly',
    remaining_percent: 42, resets_at: '2099-10-01T00:00:00.000Z',
    observed_at: '2026-09-30T18:00:00.000Z' }] };
  const unauth = await fetch(`${server.baseUrl}/api/usage-limits`);
  assert.equal(unauth.status, 401);
  const head = server.controlPlane.journal.head();
  const response = await fetch(`${server.baseUrl}/api/usage-limits`, {
    headers: { authorization: `Bearer ${server.token}` },
  });
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(compileValidator(Schema)(body).ok, true);
  assert.deepEqual(body, answer);
  assert.equal(calls, 1);
  assert.equal(server.controlPlane.journal.head(), head);
});

test('bad source data is never passed through', async () => {
  answer = { availability: 'available', readings: [{ provider: 'codex', window: 'weekly',
    remaining_percent: 101, resets_at: '2099-10-01T00:00:00.000Z',
    observed_at: '2026-09-30T18:00:00.000Z' }] };
  const response = await fetch(`${server.baseUrl}/api/usage-limits`, {
    headers: { authorization: `Bearer ${server.token}` },
  });
  const body = await response.json() as { availability: string };
  assert.equal(body.availability, 'incompatible');
});
