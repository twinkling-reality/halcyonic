import assert from 'node:assert/strict';
import {
  chmodSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import type { AgentKey } from '@halcyonic/runtime-core';
import { AGENT_KEYS } from '../runtimes.ts';
import { AgentKeys, keyFile, keyProblem, LONGEST_KEY, storedAt } from './agent-keys.ts';

/** A test adapter's declaration, said to be one: no runtime stands behind it. It proves the keys are not one vendor's. */
const TEST_AGENT_KEY: AgentKey = {
  runtimeId: 'test-agent',
  app: 'Test Agent',
  variable: 'TEST_AGENT_KEY',
  file: 'test-agent-key',
  field: 'Test Agent Key',
};

function folder(t: TestContext): string {
  const made = mkdtempSync(join(tmpdir(), 'halcyonic-agent-keys-'));
  t.after(() => rmSync(made, { recursive: true, force: true }));
  return made;
}

describe('agent keys', () => {
  test('every declared app is listed in its own words, with whether a key is stored and when, never the key', (t) => {
    const dataDir = folder(t);
    const keys = new AgentKeys(dataDir, [...AGENT_KEYS, TEST_AGENT_KEY]);
    assert.deepEqual(keys.list(), [
      {
        runtime_id: 'claude-agent',
        app: 'Claude Agent',
        field: 'Anthropic API Key',
        stored: false,
        stored_at: null,
        unusable: false,
      },
      {
        runtime_id: 'test-agent',
        app: 'Test Agent',
        field: 'Test Agent Key',
        stored: false,
        stored_at: null,
        unusable: false,
      },
    ]);
    assert.equal(keys.store('test-agent', '  test-key-not-real-1  '), null);
    const listed = keys.list();
    assert.equal(listed[0]?.stored, false, 'the other app is untouched');
    assert.equal(listed[1]?.stored, true);
    assert.ok(
      listed[1]?.stored_at !== null && !Number.isNaN(Date.parse(listed[1]?.stored_at ?? '')),
    );
    assert.equal(JSON.stringify(listed).includes('test-key-not-real'), false);
  });

  test('a key is kept whole as its private file, mode 600, over the one kept before, with nothing left beside it', (t) => {
    const dataDir = folder(t);
    const keys = new AgentKeys(dataDir, [TEST_AGENT_KEY]);
    assert.equal(keys.store('test-agent', 'test-key-not-real-1'), null);
    assert.equal(keys.store('test-agent', '\ttest-key-not-real-2\n'), null);
    const path = join(dataDir, 'test-agent-key');
    assert.equal(readFileSync(path, 'utf8'), 'test-key-not-real-2\n');
    assert.equal(lstatSync(path).mode & 0o777, 0o600);
    assert.deepEqual(readdirSync(dataDir), ['test-agent-key'], 'no temporary file stays');
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), lstatSync(path).mtime.toISOString());
  });

  test('refuses what is not one key, in words that never repeat it, and keeps nothing', (t) => {
    const dataDir = folder(t);
    const keys = new AgentKeys(dataDir, [TEST_AGENT_KEY]);
    assert.equal(keys.store('test-agent', ''), 'Paste the key first.');
    assert.equal(keys.store('test-agent', '   '), 'Paste the key first.');
    assert.equal(keys.store('test-agent', 42), 'Paste the key first.');
    assert.equal(
      keys.store('test-agent', 'test-key not-real'),
      'That has spaces or line breaks in it. Paste the key alone.',
    );
    assert.equal(
      keys.store('test-agent', 'test-key\nnot-real'),
      'That has spaces or line breaks in it. Paste the key alone.',
    );
    assert.equal(
      keys.store('test-agent', 'x'.repeat(LONGEST_KEY + 1)),
      'That is longer than any key. Paste the key alone.',
    );
    assert.equal(keyProblem('x'.repeat(LONGEST_KEY)), null);
    assert.equal(
      keys.store('some-other-agent', 'test-key-not-real'),
      'That agent app takes no key here.',
    );
    assert.equal(keys.store(undefined, 'test-key-not-real'), 'That agent app takes no key here.');
    assert.deepEqual(readdirSync(dataDir), []);
  });

  test('removes a kept key, and says so when there is none', (t) => {
    const dataDir = folder(t);
    const keys = new AgentKeys(dataDir, [TEST_AGENT_KEY]);
    assert.equal(keys.remove('test-agent'), 'No key is stored for it.');
    assert.equal(keys.store('test-agent', 'test-key-not-real'), null);
    assert.equal(keys.remove('test-agent'), null);
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
    assert.equal(keys.remove('some-other-agent'), 'That agent app takes no key here.');
  });

  test('a file placed by hand that others can read, or an empty one, is unusable, not stored, and Add Key replaces it', (t) => {
    const dataDir = folder(t);
    const keys = new AgentKeys(dataDir, [TEST_AGENT_KEY]);
    const path = join(dataDir, 'test-agent-key');
    for (const [content, mode] of [
      ['test-key-not-real\n', 0o644],
      ['  \n', 0o600],
    ] as const) {
      writeFileSync(path, content, { mode });
      chmodSync(path, mode);
      assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
      assert.deepEqual(
        keys.list().map(({ stored, unusable }) => ({ stored, unusable })),
        [{ stored: false, unusable: true }],
      );
    }
    assert.equal(keys.store('test-agent', 'test-key-not-real-2'), null);
    assert.equal(lstatSync(path).mode & 0o777, 0o600);
    assert.deepEqual(
      keys.list().map(({ stored, unusable }) => ({ stored, unusable })),
      [{ stored: true, unusable: false }],
    );
    // Remove Key takes an unusable file away too.
    writeFileSync(path, '', { mode: 0o600 });
    assert.equal(keys.remove('test-agent'), null);
    assert.equal(keyFile(dataDir, TEST_AGENT_KEY).state, 'none');
  });

  test('a folder or a link where the key file would be is not a stored key', (t) => {
    const dataDir = folder(t);
    mkdirSync(join(dataDir, 'test-agent-key'));
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
    rmSync(join(dataDir, 'test-agent-key'), { recursive: true });
    symlinkSync(join(dataDir, 'elsewhere'), join(dataDir, 'test-agent-key'));
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
  });
});
