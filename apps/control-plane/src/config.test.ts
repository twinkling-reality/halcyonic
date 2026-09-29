import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { ConfigError, loadConfig } from './config.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-config-'));
after(() => rmSync(base, { recursive: true, force: true }));

describe('configuration', () => {
  test('defaults to loopback, the standard port and no project roots', () => {
    const config = loadConfig({});
    assert.equal(config.host, '127.0.0.1');
    assert.equal(config.port, 47800);
    assert.deepEqual(config.projectRoots, []);
  });

  test('refuses a host that is not loopback', () => {
    assert.throws(() => loadConfig({ HALCYONIC_HOST: '0.0.0.0' }), ConfigError);
  });

  test('reads several project roots', () => {
    const first = join(base, 'first');
    const second = join(base, 'second');
    mkdirSync(first);
    mkdirSync(second);
    const config = loadConfig({ HALCYONIC_PROJECT_ROOTS: [first, second].join(delimiter) });
    assert.deepEqual(config.projectRoots, [first, second]);
  });

  test('real runtimes are off unless enabled, and the switches accept only 1 or 0', () => {
    assert.equal(loadConfig({}).claudeAgent, false);
    assert.equal(loadConfig({ HALCYONIC_CLAUDE_AGENT: '1' }).claudeAgent, true);
    assert.equal(loadConfig({ HALCYONIC_CLAUDE_AGENT: '0' }).claudeAgent, false);
    assert.throws(() => loadConfig({ HALCYONIC_CLAUDE_AGENT: 'yes' }), ConfigError);
  });

  test('stopping at the end of stdin is off unless enabled, and its switch accepts only 1 or 0', () => {
    assert.equal(loadConfig({}).exitOnStdinEnd, false);
    assert.equal(loadConfig({ HALCYONIC_EXIT_ON_STDIN_END: '' }).exitOnStdinEnd, false);
    assert.equal(loadConfig({ HALCYONIC_EXIT_ON_STDIN_END: '0' }).exitOnStdinEnd, false);
    assert.equal(loadConfig({ HALCYONIC_EXIT_ON_STDIN_END: '1' }).exitOnStdinEnd, true);
    for (const raw of ['yes', 'true', '2', ' 1']) {
      assert.throws(() => loadConfig({ HALCYONIC_EXIT_ON_STDIN_END: raw }), ConfigError, raw);
    }
  });

  test('a Claude Code executable must be an existing absolute file', () => {
    const executable = join(base, 'claude');
    writeFileSync(executable, '#!/bin/sh\n');
    assert.equal(
      loadConfig({ HALCYONIC_CLAUDE_EXECUTABLE: executable }).claudeExecutable,
      executable,
    );
    for (const path of ['claude', join(base, 'missing'), base]) {
      assert.throws(() => loadConfig({ HALCYONIC_CLAUDE_EXECUTABLE: path }), ConfigError, path);
    }
  });

  test('a Codex binary is off unless set, and must be an existing absolute file', () => {
    assert.equal(loadConfig({}).codexBinary, null);
    const binary = join(base, 'codex');
    writeFileSync(binary, '#!/bin/sh\n');
    assert.equal(loadConfig({ HALCYONIC_CODEX_BIN: binary }).codexBinary, binary);
    for (const path of ['codex', join(base, 'missing'), base]) {
      assert.throws(() => loadConfig({ HALCYONIC_CODEX_BIN: path }), ConfigError, path);
    }
  });

  test('agent environment pass-through takes variable names only', () => {
    assert.deepEqual(
      loadConfig({ HALCYONIC_AGENT_ENV: 'SSH_AUTH_SOCK, HTTPS_PROXY' }).agentEnvironment,
      ['SSH_AUTH_SOCK', 'HTTPS_PROXY'],
    );
    assert.throws(() => loadConfig({ HALCYONIC_AGENT_ENV: 'SSH_AUTH_SOCK=/tmp/x' }), ConfigError);
  });

  test('refuses project roots that are relative, missing or files', () => {
    const file = join(base, 'file');
    writeFileSync(file, 'x');
    for (const root of ['relative/path', join(base, 'missing'), file]) {
      assert.throws(() => loadConfig({ HALCYONIC_PROJECT_ROOTS: root }), ConfigError, root);
    }
  });
});
