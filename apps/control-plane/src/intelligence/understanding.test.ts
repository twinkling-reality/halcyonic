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
  const source = salidiumUnderstanding({ home: join(dataDir, 'salidium'), credentialPath });
  return { credentialPath, source };
}

const reason = (result: UnderstandingResult) =>
  result.availability === 'available' ? null : [result.availability, result.reason.code];

describe('Salidium as the understanding source', () => {
  test('a runtime Salidium never observes needs no credential to say so', async () => {
    const { source } = setup('unobserved');
    assert.deepEqual(reason(await source.understand('mock', 'mock-session-1')), [
      'unavailable',
      'runtime_not_observed',
    ]);
  });

  test('without a credential file the answer says how to create one', async () => {
    const { source, credentialPath } = setup('missing');
    const result = await source.understand('claude-agent', '5f0c7f1e-0000-4000-8000-000000000001');
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
    assert.deepEqual(reason(await source.understand('codex', 'thread-1')), [
      'unauthorized',
      'credential_file_exposed',
    ]);
  });

  test('with a private credential file, a Salidium that is not running reads as unavailable', async () => {
    const { source, credentialPath } = setup('not-running');
    writeFileSync(credentialPath, 'slc_example\n', { mode: 0o600 });
    const result = await source.understand('claude-agent', '5f0c7f1e-0000-4000-8000-000000000001');
    assert.deepEqual(reason(result), ['unavailable', 'not_running']);
  });
});
