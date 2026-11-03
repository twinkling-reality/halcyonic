import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { UnderstandingResult } from '@halcyonic/contracts';
import { FakeSalidium } from '@halcyonic/integration-salidium/testing';
import { salidiumUnderstanding } from './understanding.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-understanding-'));
after(() => rmSync(base, { recursive: true, force: true }));

/** Salidium at a home where it is not running, with the credential file in a data directory. */
function setup(name: string) {
  const dataDir = join(base, name);
  mkdirSync(dataDir);
  const credentialPath = join(dataDir, 'salidium-credential');
  const codexHome = join(dataDir, 'codex-home');
  const source = salidiumUnderstanding({
    home: join(dataDir, 'salidium'),
    credentialPath,
    codexHome,
  });
  return { credentialPath, codexHome, source };
}

/** When the executions asked about started, as journaled. */
const STARTED = '2026-10-07T12:00:00.000Z';
const CODEX_THREAD = '019a0000-0000-7000-8000-000000000001';

/** Writes a Codex rollout for `thread` as Codex names it, in the local date folder of STARTED. */
function writeRollout(codexHome: string, thread: string): void {
  const day = new Date(STARTED);
  const folder = join(
    codexHome,
    'sessions',
    String(day.getFullYear()),
    String(day.getMonth() + 1).padStart(2, '0'),
    String(day.getDate()).padStart(2, '0'),
  );
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, `rollout-2026-10-07T12-00-00-${thread}.jsonl`), '{}\n');
}

const reason = (result: UnderstandingResult) =>
  result.availability === 'available' ? null : [result.availability, result.reason.code];

describe('Salidium as the understanding source', () => {
  test('a runtime Salidium never observes needs no credential to say so', async () => {
    const { source } = setup('unobserved');
    assert.deepEqual(reason(await source.understand('mock', 'mock-session-1', STARTED)), [
      'unavailable',
      'runtime_not_observed',
    ]);
  });

  test("a Codex thread whose rollout is in Halcyonic's own Codex home is not observed: no credential is read", async () => {
    const { source, credentialPath, codexHome } = setup('codex-home');
    writeRollout(codexHome, CODEX_THREAD);
    // A credential other users can read would be refused for any session Salidium is asked about.
    writeFileSync(credentialPath, 'slc_example\n');
    chmodSync(credentialPath, 0o644);
    const result = await source.understand('codex', CODEX_THREAD, STARTED);
    assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    assert.match(
      result.availability === 'available' ? '' : result.reason.message,
      /Halcyonic's own Codex home/,
    );
  });

  test('a Codex thread whose rollout is in a confined Codex home is not observed: no credential is read', async () => {
    const { source, credentialPath, codexHome } = setup('codex-confined');
    writeRollout(join(codexHome, '..', 'codex-confined', '0123456789abcdef'), CODEX_THREAD);
    writeFileSync(credentialPath, 'slc_example\n');
    chmodSync(credentialPath, 0o644);
    const result = await source.understand('codex', CODEX_THREAD, STARTED);
    assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
    assert.match(
      result.availability === 'available' ? '' : result.reason.message,
      /Halcyonic's own Codex home/,
    );
  });

  test('a Codex thread without a rollout there, as one run in ~/.codex before, is asked about as before', async () => {
    const { source, credentialPath, codexHome } = setup('codex-person-home');
    writeRollout(codexHome, '019a0000-0000-7000-8000-0000000000ff');
    writeFileSync(credentialPath, 'slc_example\n');
    chmodSync(credentialPath, 0o644);
    assert.deepEqual(reason(await source.understand('codex', CODEX_THREAD, STARTED)), [
      'unauthorized',
      'credential_file_exposed',
    ]);
  });

  test('without a credential file the answer says how to create one', async () => {
    const { source, credentialPath } = setup('missing');
    const result = await source.understand(
      'claude-agent',
      '5f0c7f1e-0000-4000-8000-000000000001',
      STARTED,
    );
    assert.deepEqual(reason(result), ['unauthorized', 'credential_missing']);
    assert.match(
      result.availability === 'available' ? '' : result.reason.message,
      /salidium consumer create/,
    );
    assert.ok(
      result.availability !== 'available' && result.reason.message.includes(credentialPath),
    );
  });

  test('a credential file other users can read is refused', async () => {
    const { source, credentialPath } = setup('exposed');
    writeFileSync(credentialPath, 'slc_example\n');
    chmodSync(credentialPath, 0o644);
    assert.deepEqual(reason(await source.understand('claude-agent', 'thread-1', STARTED)), [
      'unauthorized',
      'credential_file_exposed',
    ]);
  });

  describe("OpenCode sessions in Halcyonic's own sandboxed store", () => {
    const SESSION = 'ses_1a285e0b7ffe4dQmV9bL2cXy0Z';
    let salidium: FakeSalidium;
    after(() => salidium?.close());

    /** A Salidium that says it observes OpenCode, and a data directory holding its credential. */
    async function observing(name: string) {
      salidium ??= await FakeSalidium.start();
      salidium.minor = 1;
      salidium.providers = ['claude-code', 'codex', 'salidium/opencode'];
      salidium.writeDiscovery();
      salidium.requests.length = 0;
      const dataDir = join(base, name);
      mkdirSync(dataDir);
      const credentialPath = join(dataDir, 'salidium-credential');
      writeFileSync(credentialPath, `${salidium.token}\n`, { mode: 0o600 });
      const source = salidiumUnderstanding({
        home: salidium.home,
        credentialPath,
        codexHome: join(dataDir, 'codex-home'),
      });
      // Where Halcyonic's sandboxed OpenCode server keeps its sessions (ADR 0028).
      const folder = join(dataDir, 'opencode-sandbox', 'data', 'opencode');
      return { source, folder, store: join(folder, 'opencode.db') };
    }

    test('a session started after the store was made is not observed, and Salidium is not asked', async () => {
      const { source, folder, store } = await observing('opencode-store');
      mkdirSync(folder, { recursive: true });
      writeFileSync(store, '');
      const startedAfter = new Date(Date.now() + 60_000).toISOString();
      const result = await source.understand('opencode', SESSION, startedAfter);
      assert.deepEqual(reason(result), ['unavailable', 'runtime_not_observed']);
      assert.match(
        result.availability === 'available' ? '' : result.reason.message,
        /Halcyonic's own OpenCode store/,
      );
      assert.equal(salidium.requests.length, 0, 'neither discovery nor a lookup was asked');
    });

    test('a session from before the store, as before the sandbox, is asked about as before', async () => {
      const { source, folder, store } = await observing('opencode-older-session');
      mkdirSync(folder, { recursive: true });
      writeFileSync(store, '');
      const result = await source.understand('opencode', SESSION, STARTED);
      assert.deepEqual(reason(result), ['not_found', 'not_observed']);
      assert.ok(salidium.authorizedRequests().length > 0, 'Salidium was asked');
    });

    test('a link in place of the store, or no store at all, is asked about as before', async () => {
      const linked = await observing('opencode-store-link');
      mkdirSync(linked.folder, { recursive: true });
      const elsewhere = join(base, 'opencode-store-link', 'elsewhere.db');
      writeFileSync(elsewhere, '');
      symlinkSync(elsewhere, linked.store);
      const startedAfter = new Date(Date.now() + 60_000).toISOString();
      assert.deepEqual(reason(await linked.source.understand('opencode', SESSION, startedAfter)), [
        'not_found',
        'not_observed',
      ]);
      const none = await observing('opencode-no-store');
      assert.deepEqual(reason(await none.source.understand('opencode', SESSION, startedAfter)), [
        'not_found',
        'not_observed',
      ]);
    });
  });

  test('with a private credential file, a Salidium that is not running reads as unavailable', async () => {
    const { source, credentialPath } = setup('not-running');
    writeFileSync(credentialPath, 'slc_example\n', { mode: 0o600 });
    const result = await source.understand(
      'claude-agent',
      '5f0c7f1e-0000-4000-8000-000000000001',
      STARTED,
    );
    assert.deepEqual(reason(result), ['unavailable', 'not_running']);
  });
});
