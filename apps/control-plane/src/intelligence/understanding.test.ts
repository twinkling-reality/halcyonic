import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import type { UnderstandingResult } from '@halcyonic/contracts';
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
