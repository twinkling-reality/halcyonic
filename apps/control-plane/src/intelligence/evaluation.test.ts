import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { EvaluationResult } from '@halcyonic/contracts';
import { seorakEvaluation } from './evaluation.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-evaluation-'));
after(() => rmSync(base, { recursive: true, force: true }));

const SESSION = '5f0c7f1e-0000-4000-8000-000000000001';
/** A credential in the shape Seorak issues, made up here. No test sends it to a real plane. */
const CREDENTIAL = `srkx_${randomBytes(32).toString('base64url')}\n`;

async function closedPort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  await new Promise<void>((resolve) => server.close(() => resolve()));
  return port;
}

/** Seorak on `port`, a closed one unless given, with the credential file in a data directory. */
async function setup(name: string, port?: number) {
  const dataDir = join(base, name);
  mkdirSync(dataDir);
  const credentialPath = join(dataDir, 'seorak-credential');
  const source = seorakEvaluation({ credentialPath, port: port ?? (await closedPort()) });
  return { credentialPath, source };
}

const reason = (result: EvaluationResult) =>
  result.availability === 'available' ? null : [result.availability, result.reason.code];

describe('Seorak as the evaluation source', () => {
  test('a runtime Seorak never observes needs no credential to say so', async () => {
    const { source } = await setup('unobserved');
    assert.deepEqual(reason(await source.evaluate('mock', 'mock-session-1')), [
      'unavailable',
      'runtime_not_observed',
    ]);
  });

  test("a Codex session, kept in Halcyonic's own Codex home, is not observed: no request, no credential", async (t) => {
    let requests = 0;
    const plane = createServer((_request, response) => {
      requests++;
      response.writeHead(500).end();
    });
    await new Promise<void>((resolve) => plane.listen(0, '127.0.0.1', resolve));
    t.after(async () => {
      const closed = new Promise<void>((resolve) => plane.close(() => resolve()));
      plane.closeAllConnections();
      await closed;
    });
    const { port } = plane.address() as AddressInfo;
    const { source, credentialPath } = await setup('codex-home', port);
    // A credential other users can read would be refused for any session Seorak is asked about.
    writeFileSync(credentialPath, CREDENTIAL);
    chmodSync(credentialPath, 0o644);
    const result = await source.evaluate('codex', '019a0000-0000-7000-8000-000000000001');
    assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    assert.match(
      result.availability === 'available' ? '' : result.reason.message,
      /Halcyonic's own codex home/,
    );
    assert.equal(requests, 0);
  });

  test('without a credential file the answer says how to issue one', async () => {
    const { source, credentialPath } = await setup('missing');
    const result = await source.evaluate('claude-agent', SESSION);
    assert.deepEqual(reason(result), ['unauthorized', 'credential_missing']);
    const message = result.availability === 'available' ? '' : result.reason.message;
    assert.match(message, /audience http:\/\/127\.0\.0\.1:\d+\/api\/v1/);
    assert.match(message, /sessions:read, replay:read and limits:read/);
    assert.ok(message.includes(credentialPath));
    const limits = await source.usageLimits?.();
    assert.equal(limits?.availability, 'unauthorized');
    assert.equal(limits && 'reason' in limits ? limits.reason.code : '', 'credential_missing');
  });

  test('a credential file other users can read is refused', async () => {
    const { source, credentialPath } = await setup('exposed');
    writeFileSync(credentialPath, CREDENTIAL);
    chmodSync(credentialPath, 0o644);
    assert.deepEqual(reason(await source.evaluate('claude-agent', SESSION)), [
      'unauthorized',
      'credential_file_exposed',
    ]);
  });

  test('a credential file that cannot be read says so', async () => {
    const { source, credentialPath } = await setup('unreadable');
    mkdirSync(credentialPath, { mode: 0o700 });
    assert.deepEqual(reason(await source.evaluate('claude-agent', SESSION)), [
      'unauthorized',
      'credential_unreadable',
    ]);
  });

  test('the file is read on every request, so a credential issued later needs no restart', async () => {
    const { source, credentialPath } = await setup('issued-later');
    assert.deepEqual(reason(await source.evaluate('claude-agent', SESSION)), [
      'unauthorized',
      'credential_missing',
    ]);
    writeFileSync(credentialPath, CREDENTIAL, { mode: 0o600 });
    assert.deepEqual(reason(await source.evaluate('claude-agent', SESSION)), [
      'unavailable',
      'not_running',
    ]);
  });

  test("one client serves every request, so Seorak's request budget holds across them", async (t) => {
    let requests = 0;
    const plane = createServer((_request, response) => {
      requests++;
      response.writeHead(429, { 'Content-Type': 'application/json', 'Retry-After': '60' });
      response.end('{}');
    });
    await new Promise<void>((resolve) => plane.listen(0, '127.0.0.1', resolve));
    t.after(async () => {
      const closed = new Promise<void>((resolve) => plane.close(() => resolve()));
      plane.closeAllConnections();
      await closed;
    });
    const { port } = plane.address() as AddressInfo;
    const { source, credentialPath } = await setup('shared', port);
    writeFileSync(credentialPath, CREDENTIAL, { mode: 0o600 });
    for (const session of [SESSION, SESSION.replace(/1$/, '2')])
      assert.deepEqual(reason(await source.evaluate('claude-agent', session)), [
        'unavailable',
        'rate_limited',
      ]);
    assert.equal(requests, 1);
  });
});
