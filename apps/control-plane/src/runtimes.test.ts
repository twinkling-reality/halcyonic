import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { EnvironmentError } from '@halcyonic/integration-claude-code';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { ConfigError, loadConfig } from './config.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import { ANTHROPIC_KEY_FILE, createRuntimeAdapters } from './runtimes.ts';
import { SCENARIOS } from './testing/harness.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-runtimes-'));
after(() => rmSync(base, { recursive: true, force: true }));

function adapters(
  configEnv: NodeJS.ProcessEnv,
  environment: NodeJS.ProcessEnv,
  dataDir = join(base, 'empty'),
) {
  return createRuntimeAdapters(loadConfig(configEnv), {
    mock: new MockRuntimeAdapter({ scenarios: SCENARIOS }),
    directoryPolicy: createDirectoryPolicy([]),
    environment,
    dataDir,
  });
}

const HOST = { HOME: '/home/someone', PATH: '/usr/bin' };

describe('runtime composition', () => {
  test('only the synthetic mock runtime is hosted unless a real runtime is enabled', () => {
    assert.deepEqual(
      adapters({}, {}).map((adapter) => adapter.descriptor.runtime_id),
      ['mock'],
    );
  });

  test('the Claude Agent runtime is registered when enabled and credentials exist', () => {
    const hosted = adapters(
      { HALCYONIC_CLAUDE_AGENT: '1' },
      { HOME: '/home/someone', PATH: '/usr/bin', ANTHROPIC_API_KEY: 'not-a-real-key' },
    );
    const claude = hosted.find((adapter) => adapter.descriptor.kind === 'claude-agent');
    assert.ok(claude);
    assert.equal(claude.descriptor.display_name, 'Claude Agent');
    assert.equal(claude.descriptor.synthetic, false);
  });

  test('the API key may come from a private file in the data directory instead of the environment', () => {
    const dataDir = mkdtempSync(join(base, 'key-'));
    writeFileSync(join(dataDir, ANTHROPIC_KEY_FILE), 'not-a-real-key\n', { mode: 0o600 });
    const hosted = adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, dataDir);
    assert.ok(hosted.some((adapter) => adapter.descriptor.kind === 'claude-agent'));
  });

  test('a key file other users can read, or an empty one, stops startup', () => {
    const exposed = mkdtempSync(join(base, 'exposed-'));
    writeFileSync(join(exposed, ANTHROPIC_KEY_FILE), 'not-a-real-key\n', { mode: 0o644 });
    assert.throws(() => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, exposed), ConfigError);
    const empty = mkdtempSync(join(base, 'empty-key-'));
    writeFileSync(join(empty, ANTHROPIC_KEY_FILE), '\n', { mode: 0o600 });
    assert.throws(() => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, HOST, empty), ConfigError);
  });

  test('an enabled Claude Agent runtime without credentials stops startup', () => {
    assert.throws(
      () => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, { HOME: '/home/someone', PATH: '/usr/bin' }),
      EnvironmentError,
    );
  });
});
