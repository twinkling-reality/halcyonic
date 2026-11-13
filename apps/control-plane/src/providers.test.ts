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

test('the list gives the providers by their ids, ascending, whatever order the folder is read in: none is put first by code', (t) => {
  const dir = folder(t);
  for (const id of ['zeta', 'alpha', 'mid'])
    declare(dir, id, {
      key: {
        file: `${id}-key`,
        variable: 'K',
        field: 'Key',
        header: { name: 'Authorization', scheme: 'Bearer' },
      },
    });
  assert.deepEqual(
    new HostProviders({ dir, dataDir: tmpdir() }).list().map((provider) => provider.id),
    ['alpha', 'mid', 'zeta'],
  );
});

test("a provider's key is asked of the provider itself, spending nothing, and the answer kept for that key alone", async (t) => {
  const { KeyChecks } = await import('./providers.ts');
  const dir = folder(t);
  const data = folder(t);
  chmodSync(data, 0o700);
  declare(dir, 'third');
  const providers = new HostProviders({ dir, dataDir: data });
  const asked: string[] = [];
  let answer: { ok: true } | { ok: false; code: string } = { ok: true };
  let now = 0;
  const checks = new KeyChecks({
    providers,
    protocols: new Map([
      [
        'chat_completions',
        {
          check: async (call) => {
            asked.push(`${call.model} ${call.upstream.host} ${call.key}`);
            return answer;
          },
        },
      ],
    ]),
    now: () => now,
  });
  assert.equal(await checks.state('third'), 'none', 'no key stored, nothing asked');
  assert.deepEqual(asked, []);
  writeFileSync(join(data, 'third-api-key'), 'k-one', { mode: 0o600 });
  assert.equal(await checks.state('third'), 'taken');
  assert.equal(await checks.state('third'), 'taken');
  assert.deepEqual(
    asked,
    ['third-1 127.0.0.1:1 k-one'],
    'asked once for that key, for its first model',
  );
  writeFileSync(join(data, 'third-api-key'), 'k-two', { mode: 0o600 });
  answer = { ok: false, code: 'key_refused' };
  assert.equal(await checks.state('third'), 'refused', 'a key changed since is asked again');
  answer = { ok: false, code: 'provider_unreachable' };
  now = 11 * 60_000;
  assert.equal(await checks.state('third'), 'unknown', 'an answer that is no refusal says nothing');
  assert.equal(await checks.state('third'), 'unknown');
  assert.equal(asked.length, 4, 'an unknown answer is kept for none');
  assert.equal(await checks.state('nobody'), 'unknown');
});

test('with a stand-in on this Mac, every reader of the list reaches it and no real provider: the key check, the gate and the assistant (rule 29)', async (t) => {
  const { createServer } = await import('node:http');
  const { KeyChecks } = await import('./providers.ts');
  const { gatedProviders } = await import('./runtimes.ts');
  const { PROTOCOLS } = await import('./workspace-agent/providers/index.ts');
  const seen: string[] = [];
  const standIn = createServer((req, res) => {
    seen.push(
      `${req.method} ${req.url} ${req.headers.authorization ?? req.headers['x-api-key'] ?? ''}`,
    );
    res
      .writeHead(401, { 'content-type': 'application/json' })
      .end('{"error":{"type":"authentication_error"}}');
  });
  await new Promise<void>((resolve) => standIn.listen(0, '127.0.0.1', () => resolve()));
  t.after(() => standIn.close());
  const address = `http://127.0.0.1:${(standIn.address() as { port: number }).port}`;
  const data = folder(t);
  chmodSync(data, 0o700);
  const real = new HostProviders({ dataDir: data });
  // A FAKE key for every provider the repository declares, as a lane's scratch plane might store.
  for (const provider of real.list())
    writeFileSync(join(data, provider.key.file), `fake-${provider.id}`, { mode: 0o600 });
  const providers = new HostProviders({ dataDir: data, standIn: address });
  assert.ok(providers.list().length >= 2, "the repository's own declarations");
  for (const provider of providers.list()) assert.equal(provider.upstream, address);
  const checks = new KeyChecks({ providers, protocols: PROTOCOLS });
  for (const provider of providers.list())
    assert.equal(
      await checks.state(provider.id),
      'refused',
      `${provider.id} asked of the stand-in`,
    );
  assert.equal(
    seen.length,
    providers.list().length,
    'each check reached the stand-in, so none left this Mac',
  );
  const gate = gatedProviders(providers, {
    name: 'A gated agent app (test)',
    providers: 'all',
    needs: ['chat_completions'],
    reads: 'at_start',
    turnsOn: false,
  });
  assert.ok(gate.length > 0 && gate.every((provider) => provider.upstream === address));
  assert.throws(
    () => new HostProviders({ dataDir: data, standIn: 'https://example.com/v1' }),
    /must be an address on this Mac/,
  );
});

test("a plane on a person's own data folder refuses the stand-in, so a lane's listener never gets a real key (AUDIT3)", (t) => {
  const home = folder(t);
  for (const own of ['.halcyonic', '.halcyonic-proving']) {
    const dataDir = join(home, own);
    mkdirSync(dataDir, { recursive: true });
    assert.throws(
      () =>
        new HostProviders({
          dataDir,
          standIn: 'http://127.0.0.1:9',
          ownDataFolders: [join(home, '.halcyonic'), join(home, '.halcyonic-proving')],
        }),
      /HALCYONIC_ASSISTANT_UPSTREAM is for test planes; this plane keeps a person's keys/,
      own,
    );
    // Without the stand-in, the person's own plane starts as ever.
    assert.ok(
      new HostProviders({
        dataDir,
        ownDataFolders: [join(home, '.halcyonic'), join(home, '.halcyonic-proving')],
      }).list().length > 0,
    );
  }
  // A scratch folder elsewhere takes it.
  const scratch = join(home, 'scratch-data');
  mkdirSync(scratch);
  assert.equal(
    new HostProviders({
      dataDir: scratch,
      standIn: 'http://127.0.0.1:9',
      ownDataFolders: [join(home, '.halcyonic'), join(home, '.halcyonic-proving')],
    }).list()[0]?.upstream,
    'http://127.0.0.1:9',
  );
});
