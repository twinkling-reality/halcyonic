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
import { CLAUDE_AGENT_READER } from '@halcyonic/integration-claude-code';
import { HostProviders } from '../providers.ts';
import { providersFolder, TEST_ASSISTANT_READER } from '../testing/declared-providers.ts';
import {
  keyFile,
  keyProblem,
  keysReadAtStart,
  LONGEST_KEY,
  ProviderKeys,
  storedAt,
} from './provider-keys.ts';

/** The test provider's key, as its declaration names it. */
const TEST_AGENT_KEY = { file: 'testprovider-api-key' } as const;

function folder(t: TestContext): string {
  const made = mkdtempSync(join(tmpdir(), 'halcyonic-provider-keys-'));
  t.after(() => rmSync(made, { recursive: true, force: true }));
  return made;
}

/** Keys over a folder of declarations: Anthropic's own and a test provider's, read by Claude Agent and a test assistant. */
function keysOf(
  t: TestContext,
  dataDir: string,
  extra: (declare: (id: string, name: string) => void) => void = () => undefined,
) {
  const declared = providersFolder(t);
  declared.declare('testprovider', 'Testprovider');
  extra((id, name) => declared.declare(id, name, ['responses']));
  const providers = new HostProviders({
    dir: declared.dir,
    dataDir,
    readers: [CLAUDE_AGENT_READER, TEST_ASSISTANT_READER],
  });
  return { keys: new ProviderKeys(dataDir, providers), providers };
}

describe('provider keys', () => {
  test('one card per declared provider something reads, in its own words, with its readers and when, never the key', (t) => {
    const dataDir = folder(t);
    // A provider that speaks a shape no reader here speaks is not offered: its key would do nothing.
    const { keys } = keysOf(t, dataDir, (declare) => declare('unread', 'Unread'));
    assert.deepEqual(keys.list(), [
      {
        id: 'anthropic',
        name: 'Anthropic',
        field: 'Anthropic API Key',
        readers: [
          { name: 'Claude Agent', reads: 'at_start' },
          { name: 'Test Assistant', reads: 'at_each_use' },
        ],
        applies: 'next_start',
        held_outside: [],
        stored: false,
        stored_at: null,
        unusable: false,
      },
      {
        id: 'testprovider',
        name: 'Testprovider',
        field: 'Testprovider API Key',
        readers: [{ name: 'Test Assistant', reads: 'at_each_use' }],
        applies: 'at_once',
        held_outside: [],
        stored: false,
        stored_at: null,
        unusable: false,
      },
    ]);
    assert.equal(keys.store('unread', 'test-key-not-real'), 'Nothing here takes that key.');
    assert.equal(keys.store('testprovider', '  test-key-not-real-1  '), null);
    const listed = keys.list();
    assert.equal(listed[0]?.stored, false, 'the other key is untouched');
    assert.equal(listed[1]?.stored, true);
    assert.ok(
      listed[1]?.stored_at !== null && !Number.isNaN(Date.parse(listed[1]?.stored_at ?? '')),
    );
    assert.equal(JSON.stringify(listed).includes('test-key-not-real'), false);
  });

  test("a reader whose agent app runs outside the sandbox and holds the key in its run is named on that key's card, as declared now", (t) => {
    const dataDir = folder(t);
    const declared = providersFolder(t);
    declared.declare('testprovider', 'Testprovider');
    const providers = new HostProviders({
      dir: declared.dir,
      dataDir,
      readers: [CLAUDE_AGENT_READER, TEST_ASSISTANT_READER],
    });
    let outside: readonly string[] = ['Claude Agent'];
    const keys = new ProviderKeys(dataDir, providers, () => outside);
    const cards = () => keys.list().map((card) => [card.id, card.held_outside]);
    assert.deepEqual(cards(), [
      ['anthropic', ['Claude Agent']],
      ['testprovider', []],
    ]);
    // Read at each ask: when the adapter says it runs confined, the card says nothing more.
    outside = [];
    assert.deepEqual(cards(), [
      ['anthropic', []],
      ['testprovider', []],
    ]);
  });

  test('only the keys a reader reads at its start count toward Restart to Apply', (t) => {
    const dataDir = folder(t);
    const { keys, providers } = keysOf(t, dataDir);
    const before = keysReadAtStart(dataDir, providers);
    assert.equal(before, 'anthropic=');
    assert.equal(keys.store('testprovider', 'test-key-not-real'), null);
    assert.equal(keysReadAtStart(dataDir, providers), before, 'an at-once key asks for no restart');
    assert.equal(keys.store('anthropic', 'test-key-not-real'), null);
    assert.match(keysReadAtStart(dataDir, providers), /^anthropic=\d{4}-/);
  });

  test('a key is kept whole as its private file, mode 600, over the one kept before, with nothing left beside it', (t) => {
    const dataDir = folder(t);
    const keys = keysOf(t, dataDir).keys;
    assert.equal(keys.store('testprovider', 'test-key-not-real-1'), null);
    assert.equal(keys.store('testprovider', '\ttest-key-not-real-2\n'), null);
    const path = join(dataDir, 'testprovider-api-key');
    assert.equal(readFileSync(path, 'utf8'), 'test-key-not-real-2\n');
    assert.equal(lstatSync(path).mode & 0o777, 0o600);
    assert.deepEqual(readdirSync(dataDir), ['testprovider-api-key'], 'no temporary file stays');
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), lstatSync(path).mtime.toISOString());
  });

  test('refuses what is not one key, in words that never repeat it, and keeps nothing', (t) => {
    const dataDir = folder(t);
    const keys = keysOf(t, dataDir).keys;
    assert.equal(keys.store('testprovider', ''), 'Paste the key first.');
    assert.equal(keys.store('testprovider', '   '), 'Paste the key first.');
    assert.equal(keys.store('testprovider', 42), 'Paste the key first.');
    assert.equal(
      keys.store('testprovider', 'test-key not-real'),
      'That has spaces or line breaks in it. Paste the key alone.',
    );
    assert.equal(
      keys.store('testprovider', 'test-key\nnot-real'),
      'That has spaces or line breaks in it. Paste the key alone.',
    );
    assert.equal(
      keys.store('testprovider', 'x'.repeat(LONGEST_KEY + 1)),
      'That is longer than any key. Paste the key alone.',
    );
    assert.equal(keyProblem('x'.repeat(LONGEST_KEY)), null);
    assert.equal(
      keys.store('some-other-provider', 'test-key-not-real'),
      'Nothing here takes that key.',
    );
    assert.equal(keys.store(undefined, 'test-key-not-real'), 'Nothing here takes that key.');
    assert.deepEqual(readdirSync(dataDir), []);
  });

  test('removes a kept key, and says so when there is none', (t) => {
    const dataDir = folder(t);
    const keys = keysOf(t, dataDir).keys;
    assert.equal(keys.remove('testprovider'), 'No key is stored for it.');
    assert.equal(keys.store('testprovider', 'test-key-not-real'), null);
    assert.equal(keys.remove('testprovider'), null);
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
    assert.equal(keys.remove('some-other-provider'), 'Nothing here takes that key.');
  });

  test('a file placed by hand that others can read, or an empty one, is unusable, not stored, and Add Key replaces it', (t) => {
    const dataDir = folder(t);
    const keys = keysOf(t, dataDir).keys;
    const path = join(dataDir, 'testprovider-api-key');
    for (const [content, mode] of [
      ['test-key-not-real\n', 0o644],
      ['  \n', 0o600],
    ] as const) {
      writeFileSync(path, content, { mode });
      chmodSync(path, mode);
      assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
      assert.deepEqual(
        keys
          .list()
          .filter((card) => card.id === 'testprovider')
          .map(({ stored, unusable }) => ({ stored, unusable })),
        [{ stored: false, unusable: true }],
      );
    }
    assert.equal(keys.store('testprovider', 'test-key-not-real-2'), null);
    assert.equal(lstatSync(path).mode & 0o777, 0o600);
    assert.deepEqual(
      keys
        .list()
        .filter((card) => card.id === 'testprovider')
        .map(({ stored, unusable }) => ({ stored, unusable })),
      [{ stored: true, unusable: false }],
    );
    // Remove Key takes an unusable file away too.
    writeFileSync(path, '', { mode: 0o600 });
    assert.equal(keys.remove('testprovider'), null);
    assert.equal(keyFile(dataDir, TEST_AGENT_KEY).state, 'none');
  });

  test('a folder or a link where the key file would be is not a stored key', (t) => {
    const dataDir = folder(t);
    mkdirSync(join(dataDir, 'testprovider-api-key'));
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
    rmSync(join(dataDir, 'testprovider-api-key'), { recursive: true });
    symlinkSync(join(dataDir, 'elsewhere'), join(dataDir, 'testprovider-api-key'));
    assert.equal(storedAt(dataDir, TEST_AGENT_KEY), null);
  });
});
