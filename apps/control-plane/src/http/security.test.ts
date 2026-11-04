import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  checkRequest,
  isProofChallenge,
  loadOrCreateAccessToken,
  loopbackProof,
  proofAddress,
  type RequestFacts,
} from './security.ts';

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

  test("only this server's loopback Host passes, however another is spelled (#12)", () => {
    for (const host of ['127.0.0.1:47800', 'localhost:47800', 'LOCALHOST:47800', '[::1]:47800'])
      assert.deepEqual(checkRequest({ ...allowed, host }, TOKEN), { allowed: true }, host);
    const spellings = [
      '127.0.0.1', // no port
      '127.0.0.1:47801', // another port
      '127.0.0.1:047800',
      '127.0.0.1.:47800', // a trailing dot
      'localhost.:47800',
      '127.1:47800', // short, numeric and octal forms of 127.0.0.1
      '2130706433:47800',
      '0x7f000001:47800',
      '0177.0.0.1:47800',
      '[::ffff:127.0.0.1]:47800', // IPv4 written as IPv6
      '[0:0:0:0:0:0:0:1]:47800', // ::1 written out
      '[::1]',
      'user@127.0.0.1:47800',
      '127.0.0.1:47800,evil.example',
      ' 127.0.0.1:47800',
      '',
    ];
    for (const host of spellings) {
      const decision = checkRequest({ ...allowed, host }, TOKEN);
      assert.equal(decision.allowed ? null : decision.code, 'host_not_allowed', host);
    }
  });
});

describe('loopback proof', () => {
  const challenge = 'c'.repeat(64);

  test('names the address a connection reached, IPv4 mapped into IPv6 as IPv4 (#12)', () => {
    assert.equal(proofAddress('127.0.0.1', 47800), '127.0.0.1:47800');
    assert.equal(proofAddress('::ffff:127.0.0.1', 47800), '127.0.0.1:47800');
    assert.equal(proofAddress('::1', 47800), '[::1]:47800');
    assert.equal(proofAddress('[::1]', 47800), '[::1]:47800');
  });

  test('differs for another address, port or token, so a relay cannot pass one on as its own (#12)', () => {
    const proof = loopbackProof(TOKEN, '127.0.0.1:47800', challenge);
    assert.match(proof, /^[0-9a-f]{64}$/);
    for (const other of [
      loopbackProof(TOKEN, '127.0.0.1:47801', challenge),
      loopbackProof(TOKEN, '[::1]:47800', challenge),
      loopbackProof('y'.repeat(43), '127.0.0.1:47800', challenge),
      loopbackProof(TOKEN, '127.0.0.1:47800', 'd'.repeat(64)),
    ])
      assert.notEqual(other, proof);
    assert.ok(!proof.includes(TOKEN.slice(0, 8)), 'nothing of the token');
  });

  test('answers only a fresh challenge of 32 bytes in lowercase hex (#12)', () => {
    assert.equal(isProofChallenge(challenge), true);
    for (const value of [
      'C'.repeat(64),
      'c'.repeat(63),
      'c'.repeat(65),
      `${'c'.repeat(63)}g`,
      `${'c'.repeat(64)}\n`,
      '',
      64,
      null,
      undefined,
    ])
      assert.equal(isProofChallenge(value), false, String(value));
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
