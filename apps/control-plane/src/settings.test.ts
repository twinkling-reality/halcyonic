import assert from 'node:assert/strict';
import {
  chmodSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { ConfigError, loadConfig } from './config.ts';
import {
  readHostSettings,
  SETTINGS_FILE,
  settingRoots,
  settingsInUse,
  withSettings,
  writeHostSettings,
} from './settings.ts';

const base = mkdtempSync(join(tmpdir(), 'halcyonic-settings-'));
after(() => rmSync(base, { recursive: true, force: true }));

let count = 0;
/** A data directory of the test's own, mode 700, holding `document` as its settings file. */
function dataDirWith(document?: unknown, mode = 0o600): string {
  count += 1;
  const dataDir = join(base, `data-${count}`);
  mkdirSync(dataDir, { mode: 0o700 });
  if (document !== undefined) {
    const text = typeof document === 'string' ? document : JSON.stringify(document);
    writeFileSync(join(dataDir, SETTINGS_FILE), text, { mode });
    chmodSync(join(dataDir, SETTINGS_FILE), mode);
  }
  return dataDir;
}

function folder(name: string): string {
  const path = join(base, name);
  mkdirSync(path, { recursive: true });
  return path;
}

describe('the settings file', () => {
  test('is optional: without one, the settings are empty', () => {
    assert.deepEqual(readHostSettings(dataDirWith()), {});
    assert.deepEqual(readHostSettings(join(base, 'no-such-data-dir')), {});
  });

  test('holds the project roots as a list, and what it writes it reads back the same', () => {
    const first = folder('projects');
    const second = folder('more projects');
    const dataDir = join(base, 'written', 'data');
    const path = writeHostSettings(dataDir, {
      HALCYONIC_PROJECT_ROOTS: [first, second].join(delimiter),
      HALCYONIC_NETWORK_HOST: '0.0.0.0',
    });
    assert.equal(statSync(path).mode & 0o777, 0o600);
    assert.equal(statSync(dataDir).mode & 0o777, 0o700);
    const settings = readHostSettings(dataDir);
    assert.deepEqual(settingRoots(settings), [first, second]);
    assert.equal(settings.HALCYONIC_NETWORK_HOST, '0.0.0.0');
    assert.deepEqual(
      JSON.parse(readFileSync(path, 'utf8')).HALCYONIC_PROJECT_ROOTS,
      [first, second],
      'the file keeps the roots as a list',
    );
  });

  test('is refused when other users can read or change it, or it is a link', () => {
    const settings = { format: 1, HALCYONIC_NETWORK_HOST: '0.0.0.0' };
    for (const mode of [0o644, 0o640, 0o602, 0o660]) {
      assert.throws(
        () => readHostSettings(dataDirWith(settings, mode)),
        /chmod 600/,
        mode.toString(8),
      );
    }
    const target = dataDirWith(settings);
    const linked = dataDirWith();
    symlinkSync(join(target, SETTINGS_FILE), join(linked, SETTINGS_FILE));
    assert.throws(() => readHostSettings(linked), /is a link/);
  });

  test('is refused in a data directory other users can change', () => {
    const dataDir = dataDirWith({ format: 1 });
    chmodSync(dataDir, 0o770);
    assert.throws(() => readHostSettings(dataDir), /chmod 700/);
    chmodSync(dataDir, 0o700);
    assert.deepEqual(readHostSettings(dataDir), {});
  });

  test('can never turn on Claude Agent, pass variables to agents, or set anything else it does not know', () => {
    for (const name of [
      'HALCYONIC_CLAUDE_AGENT',
      'HALCYONIC_CLAUDE_EXECUTABLE',
      'HALCYONIC_AGENT_ENV',
    ]) {
      assert.throws(
        () => readHostSettings(dataDirWith({ format: 1, [name]: '1' })),
        (error: unknown) =>
          error instanceof ConfigError &&
          /paid model use, so only the environment sets it/.test(error.message),
        name,
      );
    }
    for (const name of [
      'HALCYONIC_DATA_DIR',
      'HALCYONIC_HOST',
      'HALCYONIC_MODEL',
      'model',
      'ANTHROPIC_API_KEY',
      'OPENAI_API_KEY',
      'PATH',
    ]) {
      assert.throws(
        () => readHostSettings(dataDirWith({ format: 1, [name]: 'x' })),
        /can't set/,
        name,
      );
    }
  });

  test('must say its format and hold well-formed values', () => {
    for (const document of [
      '{"format": 1',
      '[]',
      'null',
      {},
      { format: 2 },
      { format: '1' },
      { format: 1, HALCYONIC_PROJECT_ROOTS: '/a' },
      { format: 1, HALCYONIC_PROJECT_ROOTS: [''] },
      { format: 1, HALCYONIC_PROJECT_ROOTS: [`/a${delimiter}/b`] },
      { format: 1, HALCYONIC_PROJECT_ROOTS: [1] },
      { format: 1, HALCYONIC_NETWORK_HOST: '' },
      { format: 1, HALCYONIC_NETWORK_HOST: ['0.0.0.0'] },
      { format: 1, HALCYONIC_OPENCODE_BIN: null },
    ]) {
      assert.throws(
        () => readHostSettings(dataDirWith(document)),
        ConfigError,
        JSON.stringify(document),
      );
    }
    assert.throws(
      () => readHostSettings(dataDirWith(`{"format": 1, "x": "${'y'.repeat(70_000)}"}`)),
      /larger than/,
    );
  });

  test('fills in only what the environment leaves unset; a variable in the environment wins, even empty', () => {
    const projects = folder('env-wins');
    const settings = { HALCYONIC_PROJECT_ROOTS: projects, HALCYONIC_NETWORK_HOST: '0.0.0.0' };
    assert.deepEqual(loadConfig(withSettings({}, settings)).projectRoots, [projects]);
    assert.deepEqual(
      loadConfig(withSettings({ HALCYONIC_PROJECT_ROOTS: '' }, settings)).projectRoots,
      [],
    );
    assert.equal(loadConfig(withSettings({ HALCYONIC_NETWORK_HOST: '' }, settings)).network, null);
    assert.deepEqual(settingsInUse({ HALCYONIC_NETWORK_HOST: '' }, settings), [
      'HALCYONIC_PROJECT_ROOTS',
    ]);
    assert.deepEqual(settingsInUse({}, {}), []);
  });
});
