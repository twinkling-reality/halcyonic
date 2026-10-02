import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { ConfigError, loadConfig, readOpenCodeSettings } from './config.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-config-'));
after(() => rmSync(base, { recursive: true, force: true }));

let homes = 0;
/** Halcyonic's own OpenCode settings as local-model writes them: folders 700, the file 600. */
function openCodeHome(settings: unknown): string {
  homes += 1;
  const home = join(base, `opencode-home-${homes}`);
  mkdirSync(join(home, 'opencode'), { recursive: true, mode: 0o700 });
  chmodSync(home, 0o700);
  if (settings !== undefined) {
    writeFileSync(join(home, 'opencode', 'opencode.json'), JSON.stringify(settings), {
      mode: 0o600,
    });
  }
  return home;
}

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

  test('serves nothing on the network unless the owner names an address to listen on', () => {
    assert.equal(loadConfig({}).network, null);
    assert.equal(loadConfig({ HALCYONIC_NETWORK_HOST: '' }).network, null);
    assert.throws(() => loadConfig({ HALCYONIC_NETWORK_PORT: '47801' }), /network listener is off/);
    assert.deepEqual(loadConfig({ HALCYONIC_NETWORK_HOST: '0.0.0.0' }).network, {
      host: '0.0.0.0',
      port: 47801,
    });
    assert.deepEqual(
      loadConfig({ HALCYONIC_NETWORK_HOST: '192.168.1.23', HALCYONIC_NETWORK_PORT: '48001' })
        .network,
      { host: '192.168.1.23', port: 48001 },
    );
    assert.deepEqual(loadConfig({ HALCYONIC_NETWORK_HOST: '::' }).network, {
      host: '::',
      port: 47801,
    });
  });

  test('the network listener takes an IP address and a port of its own', () => {
    for (const host of ['localhost', 'my-mac.local', '192.168.1', ' 0.0.0.0']) {
      assert.throws(() => loadConfig({ HALCYONIC_NETWORK_HOST: host }), ConfigError, host);
    }
    assert.throws(
      () => loadConfig({ HALCYONIC_NETWORK_HOST: '0.0.0.0', HALCYONIC_NETWORK_PORT: '47800' }),
      /must differ/,
    );
    assert.throws(
      () => loadConfig({ HALCYONIC_NETWORK_HOST: '0.0.0.0', HALCYONIC_NETWORK_PORT: '70000' }),
      ConfigError,
    );
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

  test('voice is off unless the whisper.cpp binary, model and voice activity model are all set, as existing absolute files', () => {
    assert.equal(loadConfig({}).speech, null);
    const files = ['whisper-cli', 'model.bin', 'vad.bin'].map((name) => {
      const path = join(base, name);
      writeFileSync(path, '');
      return path;
    });
    const [binary, model, vadModel] = files as [string, string, string];
    const all = {
      HALCYONIC_WHISPER_BIN: binary,
      HALCYONIC_WHISPER_MODEL: model,
      HALCYONIC_WHISPER_VAD_MODEL: vadModel,
    };
    assert.deepEqual(loadConfig(all).speech, { binary, model, vadModel });
    for (const name of Object.keys(all)) {
      assert.throws(() => loadConfig({ ...all, [name]: '' }), /Voice needs all of/, name);
      for (const path of ['relative', join(base, 'missing'), base]) {
        assert.throws(() => loadConfig({ ...all, [name]: path }), ConfigError, `${name} ${path}`);
      }
    }
  });

  test("Halcyonic's own OpenCode settings must name a model served on this Mac as the default", () => {
    assert.equal(loadConfig({}).opencodeConfigHome, null);
    const local = openCodeHome({ model: 'ollama/qwen3.6:35b-a3b-nvfp4' });
    assert.equal(loadConfig({ HALCYONIC_OPENCODE_CONFIG_HOME: local }).opencodeConfigHome, local);
    const written = openCodeHome({
      model: 'ollama/qwen3.6:35b-a3b-nvfp4',
      small_model: 'ollama/qwen3.6:35b-a3b-nvfp4',
      permissions: [
        { action: 'shell', resource: '*', effect: 'ask' },
        { action: 'webfetch', resource: '*', effect: 'deny' },
      ],
      providers: {
        ollama: {
          models: { 'qwen3.6:35b-a3b-nvfp4': { limit: { context: 65536, output: 16384 } } },
        },
      },
    });
    assert.deepEqual(readOpenCodeSettings(written).permissions[1], {
      action: 'webfetch',
      resource: '*',
      effect: 'deny',
    });
    for (const [why, settings] of [
      ['no default model', { permissions: [] }],
      ['a remote model of OpenCode', { model: 'opencode/space-bunny-free' }],
      ['a remote provider', { model: 'anthropic/claude-opus-5-5' }],
      ['an Ollama cloud model', { model: 'ollama/gpt-oss:120b-cloud' }],
      ['an Ollama cloud tag', { model: 'ollama/qwen3:cloud' }],
      [
        'a remote small model',
        { model: 'ollama/llama3.2:1b', small_model: 'opencode/space-bunny-free' },
      ],
      ['a key local-model never writes', { model: 'ollama/llama3.2:1b', plugin: ['x'] }],
      ['an MCP server', { model: 'ollama/llama3.2:1b', mcp: {} }],
      [
        'a malformed rule',
        { model: 'ollama/llama3.2:1b', permissions: [{ action: 'shell', effect: 'maybe' }] },
      ],
      [
        'a remote provider beside Ollama',
        { model: 'ollama/llama3.2:1b', providers: { anthropic: {} } },
      ],
      [
        'a provider setting',
        {
          model: 'ollama/llama3.2:1b',
          providers: { ollama: { settings: { baseURL: 'https://x' } } },
        },
      ],
    ] as const) {
      assert.throws(
        () => loadConfig({ HALCYONIC_OPENCODE_CONFIG_HOME: openCodeHome(settings) }),
        ConfigError,
        why,
      );
    }
  });

  test("Halcyonic's own OpenCode settings are refused unless only this user can read or change them", () => {
    const settings = { model: 'ollama/llama3.2:1b' };
    const refused = (why: string, home: string) =>
      assert.throws(() => loadConfig({ HALCYONIC_OPENCODE_CONFIG_HOME: home }), ConfigError, why);
    refused('no settings file', openCodeHome(undefined));
    refused('a relative path', 'opencode-home');
    let home = openCodeHome(settings);
    chmodSync(join(home, 'opencode', 'opencode.json'), 0o644);
    refused('a file others can read', home);
    home = openCodeHome(settings);
    chmodSync(join(home, 'opencode'), 0o755);
    refused('a folder others can read', home);
    home = openCodeHome(settings);
    chmodSync(home, 0o777);
    refused('a home anyone can change', home);
    home = openCodeHome(settings);
    const elsewhere = openCodeHome(settings);
    rmSync(join(home, 'opencode', 'opencode.json'));
    symlinkSync(
      join(elsewhere, 'opencode', 'opencode.json'),
      join(home, 'opencode', 'opencode.json'),
    );
    refused('a settings file that is a link', home);
    for (const other of ['opencode.jsonc', 'config.json', 'plugin']) {
      home = openCodeHome(settings);
      writeFileSync(join(home, 'opencode', other), '{}', { mode: 0o600 });
      refused(`another file beside it: ${other}`, home);
    }
    // Tools an agent runs inherit the same configuration home and may keep folders beside OpenCode's.
    home = openCodeHome(settings);
    mkdirSync(join(home, 'configstore'), { mode: 0o755 });
    assert.equal(loadConfig({ HALCYONIC_OPENCODE_CONFIG_HOME: home }).opencodeConfigHome, home);
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
