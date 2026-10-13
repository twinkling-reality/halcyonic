import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const panel = join(root, 'apps/xr/Android/panel');
const sources = join(panel, 'src/com/halcyonic/panel');
const android = '/Applications/Unity/Hub/Editor/6000.3.25f1/PlaybackEngines/AndroidPlayer';
const bin = process.env.JAVA_HOME
  ? join(process.env.JAVA_HOME, 'bin')
  : join(android, 'OpenJDK/bin');
const jar = process.env.ANDROID_JAR ?? join(android, 'SDK/platforms/android-36/android.jar');
let classes: string;
let recording: string;

describe('native project output links', () => {
  before(() => {
    assert.ok(
      existsSync(join(bin, 'javac')) && existsSync(jar),
      'JDK and Android API jar are required',
    );
    classes = mkdtempSync(join(tmpdir(), 'halcyonic-output-'));
    execFileSync(join(bin, 'javac'), [
      '--release',
      '11',
      '-cp',
      jar,
      '-d',
      classes,
      ...readdirSync(sources)
        .filter((name) => name.endsWith('.java'))
        .map((name) => join(sources, name)),
      join(panel, 'test/com/halcyonic/panel/PanelOutputSelfTest.java'),
    ]);
    recording = mkdtempSync(join(tmpdir(), 'halcyonic-launcher-recording-'));
    const stubs = join(panel, 'test/output-stubs');
    execFileSync(join(bin, 'javac'), [
      '--release',
      '11',
      '-cp',
      classes,
      '-d',
      recording,
      ...readdirSync(stubs, { recursive: true, encoding: 'utf8' })
        .filter((name) => name.endsWith('.java'))
        .map((name) => join(stubs, name)),
      join(panel, 'test/com/halcyonic/panel/PanelLauncherSelfTest.java'),
    ]);
  });
  after(() => {
    for (const path of [classes, recording])
      if (path) rmSync(path, { recursive: true, force: true });
  });
  for (const name of ['store', 'selection', 'lease', 'open']) {
    test(`consumed ${name} boundary`, () => {
      assert.equal(
        execFileSync(
          join(bin, 'java'),
          ['-cp', classes, 'com.halcyonic.panel.PanelOutputSelfTest', name],
          { encoding: 'utf8' },
        ).trim(),
        'passed',
      );
    });
  }
  test('API-compiled launcher adapter filters candidates and sends only the explicit normal launch intent', () => {
    assert.equal(
      execFileSync(
        join(bin, 'java'),
        [
          '-cp',
          [recording, classes].join(process.platform === 'win32' ? ';' : ':'),
          'com.halcyonic.panel.PanelLauncherSelfTest',
        ],
        { encoding: 'utf8' },
      ).trim(),
      'passed',
    );
  });
});
