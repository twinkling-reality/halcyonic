import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import type { KeyReader } from '@halcyonic/runtime-core';
import { ConfigError } from './config.ts';
import { HostProviders } from './providers.ts';

function folder(t: { after: (f: () => void) => void }): string {
  const dir = mkdtempSync(join(tmpdir(), 'halcyonic-providers-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  return dir;
}

/** A provider declaration as a file, nothing else: the one thing a third provider is (ADR 0061). */
function declare(dir: string, id: string, overrides: Record<string, unknown> = {}): void {
  mkdirSync(join(dir, id), { recursive: true });
  writeFileSync(
    join(dir, id, 'provider.json'),
    JSON.stringify({
      format: 1,
      id,
      name: 'Third',
      upstream: 'http://127.0.0.1:1/v1',
      speaks: ['chat_completions', 'responses'],
      key: {
        file: `${id}-api-key`,
        variable: 'THIRD_API_KEY',
        field: 'Third API Key',
        header: { name: 'Authorization', scheme: 'Bearer' },
      },
      models: ['third-1'],
      ...overrides,
    }),
  );
}

test("the repository's own declarations hold, and name no vendor outside their own folders", () => {
  const providers = new HostProviders({ dataDir: tmpdir() });
  const anthropic = providers.byId('anthropic');
  assert.ok(anthropic !== undefined);
  assert.deepEqual(anthropic.speaks, ['messages']);
  assert.equal(anthropic.key.file, 'anthropic-api-key');
  assert.equal(anthropic.key.field, 'Anthropic API Key');
});

test('a third provider is one declaration file: the list, the readers that reach it and its key state follow from it alone', (t) => {
  const dir = folder(t);
  const data = folder(t);
  chmodSync(data, 0o700);
  declare(dir, 'third');
  const chat: KeyReader = {
    name: 'An agent app that speaks Chat Completions (test)',
    providers: 'all',
    needs: ['chat_completions'],
    reads: 'at_start',
    turnsOn: false,
  };
  const messagesOnly: KeyReader = {
    name: 'A reader that speaks Messages only (test)',
    providers: 'all',
    needs: ['messages'],
    reads: 'at_each_use',
    turnsOn: false,
  };
  const providers = new HostProviders({ dir, dataDir: data, readers: [chat, messagesOnly] });
  assert.deepEqual(
    providers.list().map((provider) => provider.id),
    ['third'],
  );
  assert.deepEqual(
    providers.serving(['responses']).map((provider) => provider.id),
    ['third'],
  );
  assert.deepEqual(
    providers.readersOf('third').map((reader) => reader.name),
    [chat.name],
    'a reader reaches a provider only through a shape both speak',
  );
  assert.deepEqual(providers.keyState('third'), { kind: 'none' });
  writeFileSync(join(data, 'third-api-key'), 'k-test-not-a-key\n', { mode: 0o600 });
  assert.deepEqual(providers.keyState('third'), { kind: 'stored', value: 'k-test-not-a-key' });
  chmodSync(join(data, 'third-api-key'), 0o644);
  assert.equal(providers.keyState('third').kind, 'unusable', 'a key others can read is not used');
});

test('a declaration that does not hold stops the start, saying which file and why', (t) => {
  const cases: [string, Record<string, unknown>, RegExp][] = [
    ['wrong-id', { id: 'other' }, /id must be the folder name/],
    ['plain-http', { upstream: 'http://example.com/v1' }, /https/],
    ['unknown-shape', { speaks: ['telepathy'] }, /speaks must name/],
    ['extra', { colour: 'blue' }, /unknown fields: colour/],
    [
      'bad-header',
      { key: { file: 'f', variable: 'V', field: 'F', header: { name: 'X' } } },
      /key.header/,
    ],
  ];
  for (const [id, overrides, why] of cases) {
    const dir = folder(t);
    declare(dir, id, overrides);
    assert.throws(
      () => new HostProviders({ dir, dataDir: tmpdir() }),
      (error: unknown) => error instanceof ConfigError && why.test(error.message),
      id,
    );
  }
  const dir = folder(t);
  declare(dir, 'one', {
    key: { file: 'shared-key', variable: 'A', field: 'A', header: { name: 'X', scheme: null } },
  });
  declare(dir, 'two', {
    key: { file: 'shared-key', variable: 'B', field: 'B', header: { name: 'X', scheme: null } },
  });
  assert.throws(() => new HostProviders({ dir, dataDir: tmpdir() }), /same key file/);
});
