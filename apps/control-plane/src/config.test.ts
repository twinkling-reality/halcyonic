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

  test('refuses project roots that are relative, missing or files', () => {
    const file = join(base, 'file');
    writeFileSync(file, 'x');
    for (const root of ['relative/path', join(base, 'missing'), file]) {
      assert.throws(() => loadConfig({ HALCYONIC_PROJECT_ROOTS: root }), ConfigError, root);
    }
  });
});
