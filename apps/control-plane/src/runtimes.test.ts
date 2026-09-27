import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { ClaudeAgentRuntimeAdapter, EnvironmentError } from '@halcyonic/integration-claude-code';
import { MockRuntimeAdapter } from '@halcyonic/integration-mock';
import { ConfigError, loadConfig } from './config.ts';
import { createDirectoryPolicy } from './directory-policy.ts';
import {
  ANTHROPIC_KEY_FILE,
  CLAUDE_AGENT_PROCESS_RECORD,
  claudeAgentEnvironment,
  createRuntimeAdapters,
  stopStaleRuntimeServers,
} from './runtimes.ts';
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

  test('the OpenCode runtime is registered when its binary is configured, without launching it', async () => {
    const dataDir = mkdtempSync(join(base, 'opencode-'));
    const binary = join(dataDir, 'opencode');
    writeFileSync(binary, '#!/bin/sh\nexit 1\n', { mode: 0o755 });
    const hosted = adapters({ HALCYONIC_OPENCODE_BIN: binary }, HOST, dataDir);
    const opencode = hosted.find((adapter) => adapter.descriptor.kind === 'opencode');
    assert.ok(opencode);
    assert.equal(opencode.descriptor.synthetic, false);
    assert.deepEqual(
      await stopStaleRuntimeServers(hosted),
      [],
      'nothing was recorded, so nothing is stopped',
    );
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('a Claude Code process recorded by an earlier run is stopped at startup, without launching anything', async (t) => {
    const dataDir = mkdtempSync(join(base, 'claude-stale-'));
    // Plays a Claude Code process that outlived a crashed control plane.
    const sessionId = randomUUID();
    const orphan = spawn(
      process.execPath,
      ['-e', 'setInterval(() => {}, 1000)', '--', `--session-id=${sessionId}`],
      { stdio: 'ignore' },
    );
    const stoppedBy = new Promise((resolve) =>
      orphan.once('exit', (_code, signal) => resolve(signal)),
    );
    t.after(() => orphan.kill('SIGKILL'));
    const pid = orphan.pid;
    assert.ok(pid !== undefined);
    // Recorded as the adapter records a launch: the start time and command line `ps` reports.
    const fields = execFileSync('ps', ['-ww', '-p', String(pid), '-o', 'lstart=,args='], {
      env: { PATH: '/bin:/usr/bin', TZ: 'UTC', LC_ALL: 'C' },
      encoding: 'utf8',
    })
      .trim()
      .split(/\s+/);
    const record = join(dataDir, CLAUDE_AGENT_PROCESS_RECORD);
    const startedAt = fields.slice(0, 5).join(' ');
    const command = fields.slice(5).join(' ');
    writeFileSync(record, JSON.stringify({ processes: [{ pid, startedAt, command, sessionId }] }), {
      mode: 0o600,
    });

    const hosted = adapters(
      { HALCYONIC_CLAUDE_AGENT: '1' },
      { ...HOST, ANTHROPIC_API_KEY: 'not-a-real-key' },
      dataDir,
    );
    assert.deepEqual(await stopStaleRuntimeServers(hosted), [
      { runtimeId: 'claude-agent', outcome: 'stopped', pid },
    ]);
    assert.equal(await stoppedBy, 'SIGTERM');
    assert.equal(existsSync(record), false);
    const claude = hosted.find((adapter) => adapter instanceof ClaudeAgentRuntimeAdapter);
    assert.ok(claude instanceof ClaudeAgentRuntimeAdapter);
    assert.equal(claude.watchdogPid, null, 'nothing was launched');
    await Promise.all(hosted.map((adapter) => adapter.close()));
  });

  test('launched Claude Code sessions carry the Halcyonic launcher label, which pass-through cannot override', () => {
    assert.deepEqual(
      claudeAgentEnvironment({ SSH_AUTH_SOCK: '/tmp/agent', SEORAK_LAUNCHER: 'other' }),
      {
        SSH_AUTH_SOCK: '/tmp/agent',
        SEORAK_LAUNCHER: 'halcyonic',
      },
    );
  });

  test('an enabled Claude Agent runtime without credentials stops startup', () => {
    assert.throws(
      () => adapters({ HALCYONIC_CLAUDE_AGENT: '1' }, { HOME: '/home/someone', PATH: '/usr/bin' }),
      EnvironmentError,
    );
  });
});
