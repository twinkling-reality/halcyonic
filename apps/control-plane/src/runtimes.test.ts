import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { EnvironmentError } from '@halcyonic/integration-claude-code';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { loadConfig } from './config.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import { createRuntimeAdapters } from './runtimes.ts';
import { SCENARIOS } from './testing/harness.ts';

function adapters(configEnv: NodeJS.ProcessEnv, environment: NodeJS.ProcessEnv) {
  return createRuntimeAdapters(loadConfig(configEnv), {
    mock: new MockRuntimeAdapter({ scenarios: SCENARIOS }),
    directoryPolicy: createDirectoryPolicy([]),
    environment,
  });
}

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

  test('an enabled Claude Agent runtime without credentials stops startup', () => {
    assert.throws(
      () => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, { HOME: '/home/someone', PATH: '/usr/bin' }),
      EnvironmentError,
    );
  });
});
