import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { checkRequest, loadOrCreateAccessToken, type RequestFacts } from './security.ts';

const TOKEN = 'x'.repeat(43);
const allowed: RequestFacts = {
  host: '127.0.0.1:47800',
  origin: undefined,
  secFetchSite: undefined,
  authorization: `Bearer ${TOKEN}`,
  path: '/api/snapshot',
  localPort: 47800,
};

describe('request guard', () => {
  test('a loopback request with the token is allowed', () => {
    assert.deepEqual(checkRequest(allowed, TOKEN), { allowed: true });
    assert.deepEqual(checkRequest({ ...allowed, host: 'localhost:47800' }, TOKEN), {
      allowed: true,
    });
  });

  test('a foreign Host is refused, which defeats DNS rebinding', () => {
    for (const host of ['evil.example:47800', '127.0.0.1:8080', undefined]) {
      const decision = checkRequest({ ...allowed, host }, TOKEN);
      assert.equal(decision.allowed ? null : decision.code, 'host_not_allowed', String(host));
    }
  });

  test('browser requests are refused even with the token', () => {
    const withOrigin = checkRequest({ ...allowed, origin: 'https://evil.example' }, TOKEN);
    assert.equal(withOrigin.allowed ? null : withOrigin.code, 'origin_not_allowed');
    const crossSite = checkRequest({ ...allowed, secFetchSite: 'cross-site' }, TOKEN);
    assert.equal(crossSite.allowed ? null : crossSite.code, 'cross_site_request');
  });

  test('a missing or wrong token is refused except on the health path', () => {
    for (const authorization of [undefined, `Bearer ${'y'.repeat(43)}`, TOKEN, 'Basic abc']) {
      const decision = checkRequest({ ...allowed, authorization }, TOKEN);
      assert.equal(decision.allowed ? null : decision.status, 401);
    }
    assert.deepEqual(
      checkRequest({ ...allowed, authorization: undefined, path: '/api/health' }, TOKEN),
      { allowed: true },
    );
  });
});

describe('access token file', () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-token-'));
  after(() => rmSync(directory, { recursive: true, force: true }));

  test('is created once with owner-only permissions and then reused', async () => {
    const created = await loadOrCreateAccessToken(directory);
    assert.equal(created.created, true);
    assert.ok(created.token.length >= 32);
    assert.equal(statSync(created.path).mode & 0o777, 0o600);
    const reused = await loadOrCreateAccessToken(directory);
    assert.deepEqual(
      { token: reused.token, created: reused.created },
      { token: created.token, created: false },
    );
  });

  test('a truncated token file is an error rather than a silent replacement', async () => {
    const other = mkdtempSync(join(tmpdir(), 'halcyonic-token-'));
    try {
      writeFileSync(join(other, 'access-token'), 'short\n');
      await assert.rejects(loadOrCreateAccessToken(other), /does not contain a valid access token/);
    } finally {
      rmSync(other, { recursive: true, force: true });
    }
  });
});
