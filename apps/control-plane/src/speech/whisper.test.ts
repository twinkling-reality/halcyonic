import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { SpeechConfig } from '../config.ts';
import { writeWav } from './wav.ts';
import { PROMPT_WORDS, WhisperEngine } from './whisper.ts';

const scratch = mkdtempSync(join(tmpdir(), 'halcyonic-whisper-test-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

interface Launch {
  readonly args: string[];
  readonly env: string[];
  readonly clip: string;
  readonly clipMode: number;
  readonly directoryMode: number;
  readonly clipBytes: number;
  readonly pid: number;
}

/** Whether a process is still alive, waiting up to a second for one just killed to go. */
async function alive(pid: number): Promise<boolean> {
  for (let attempt = 0; attempt < 20; attempt++) {
    try {
      process.kill(pid, 0);
    } catch {
      return false;
    }
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  return true;
}

/**
 * A stand-in for whisper-cli: reports a version, records how it was launched and what it was
 * given, then does what `behaviour` says.
 */
function fakeWhisper(
  name: string,
  behaviour: string,
  version = '1.9.4-dev',
): { config: SpeechConfig; launches: () => Launch[]; record: string } {
  const record = join(scratch, `${name}.jsonl`);
  const binary = join(scratch, `${name}.cjs`);
  writeFileSync(
    binary,
    `#!${process.execPath}
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
if (args[0] === '--version') { process.stdout.write('whisper.cpp version: ${version}\\n'); process.exit(0); }
const clip = args[args.indexOf('-f') + 1];
fs.appendFileSync(${JSON.stringify(record)}, JSON.stringify({
  args, env: Object.keys(process.env), clip,
  clipMode: fs.statSync(clip).mode & 0o777,
  directoryMode: fs.statSync(path.dirname(clip)).mode & 0o777,
  clipBytes: fs.statSync(clip).size,
  pid: process.pid,
}) + '\\n');
${behaviour}
`,
  );
  chmodSync(binary, 0o755);
  return {
    record,
    config: { binary, model: join(scratch, 'model.bin'), vadModel: join(scratch, 'vad.bin') },
    launches: () =>
      existsSync(record)
        ? readFileSync(record, 'utf8')
            .trim()
            .split('\n')
            .map((line) => JSON.parse(line) as Launch)
        : [],
  };
}

const clip = writeWav(Buffer.alloc(32_000));

describe('whisper.cpp launched for one clip', () => {
  test('reports its version, and a binary that does not, or is not 1.9.4, stops startup', async () => {
    const { config } = fakeWhisper('version', '');
    assert.deepEqual((await WhisperEngine.open(config)).engine, {
      name: 'whisper.cpp',
      version: '1.9.4-dev',
    });
    const tagged = fakeWhisper('version-tagged', '', '1.9.4');
    assert.equal((await WhisperEngine.open(tagged.config)).engine.version, '1.9.4');
    for (const other of ['1.10.0', '1.9.40', '1.9.4-rc1']) {
      await assert.rejects(
        WhisperEngine.open(fakeWhisper(`version-${other}`, '', other).config),
        /Halcyonic runs whisper\.cpp 1\.9\.4/,
        other,
      );
    }
    const silent = join(scratch, 'not-whisper.cjs');
    writeFileSync(silent, `#!${process.execPath}\nprocess.stdout.write('hello\\n');\n`);
    chmodSync(silent, 0o755);
    await assert.rejects(
      WhisperEngine.open({ ...config, binary: silent }),
      /did not report a whisper\.cpp version/,
    );
  });

  test('runs in English with the prompt and voice activity detection, with no environment, from a private file removed after', async () => {
    const { config, launches } = fakeWhisper(
      'speak',
      "process.stdout.write('\\n Use TypeScript  instead.\\n');",
    );
    const engine = await WhisperEngine.open(config);
    assert.deepEqual(await engine.transcribe(clip), {
      kind: 'text',
      text: 'Use TypeScript instead.',
    });
    const [launch] = launches();
    assert.ok(launch);
    assert.deepEqual(launch.args, [
      '-m',
      config.model,
      '-l',
      'en',
      '-nt',
      '-np',
      '--prompt',
      `${PROMPT_WORDS.join(', ')}.`,
      '--vad',
      '-vm',
      config.vadModel,
      '-vp',
      '500',
      '-f',
      launch.clip,
    ]);
    assert.equal(launch.env.includes('PATH'), false);
    assert.equal(launch.env.includes('HOME'), false);
    assert.equal(launch.clipMode, 0o600);
    assert.equal(launch.directoryMode, 0o700);
    assert.equal(launch.clipBytes, clip.length);
    assert.equal(dirname(launch.clip).startsWith(join(tmpdir(), 'halcyonic-speech-')), true);
    assert.equal(existsSync(dirname(launch.clip)), false, 'the clip and its directory are removed');
  });

  test('no output is no text, which the caller answers as nothing heard', async () => {
    const { config } = fakeWhisper('silent', '');
    assert.deepEqual(await (await WhisperEngine.open(config)).transcribe(clip), {
      kind: 'text',
      text: '',
    });
  });

  test('an engine that fails, runs too long or writes too much gives a failure, and its clip is still removed', async () => {
    const cases = [
      ['fails', 'process.exit(3);', 'The engine stopped with an error.'],
      ['hangs', 'setTimeout(() => {}, 60_000);', 'The engine took longer than 0.3 s.'],
      [
        'floods',
        "process.stdout.write('x'.repeat(200_000));",
        'The engine wrote more than a transcript.',
      ],
    ] as const;
    for (const [name, behaviour, message] of cases) {
      const { config, launches } = fakeWhisper(name, behaviour);
      const engine = await WhisperEngine.open(config, 300);
      assert.deepEqual(await engine.transcribe(clip), { kind: 'failed', message }, name);
      const launch = launches()[0];
      assert.ok(launch);
      assert.equal(existsSync(dirname(launch.clip)), false, `${name}: the clip is removed`);
    }
  });

  test('a timeout kills the engine with everything it started, and answers at once', async () => {
    const { config, launches } = fakeWhisper(
      'forks',
      `const kid = require('node:child_process').spawn(process.execPath, ['-e', 'setTimeout(() => {}, 30000)'], { stdio: ['ignore', 'inherit', 'ignore'] });
fs.writeFileSync(${JSON.stringify(`${join(scratch, 'forks')}.grandchild`)}, String(kid.pid));
setTimeout(() => {}, 30000);`,
    );
    const engine = await WhisperEngine.open(config, 300);
    const started = performance.now();
    assert.deepEqual(await engine.transcribe(clip), {
      kind: 'failed',
      message: 'The engine took longer than 0.3 s.',
    });
    assert.ok(performance.now() - started < 3_000, 'not held open by what the engine started');
    const launch = launches()[0];
    assert.ok(launch);
    assert.equal(await alive(launch.pid), false);
    assert.equal(
      await alive(Number(readFileSync(`${join(scratch, 'forks')}.grandchild`, 'utf8'))),
      false,
    );
    assert.equal(existsSync(dirname(launch.clip)), false);
  });

  test('the warm-up has longer than a clip, for the first compile of the GPU shaders', async () => {
    const { config } = fakeWhisper('slow', 'setTimeout(() => process.exit(0), 700);');
    const engine = await WhisperEngine.open(config, 300, 5_000);
    assert.equal((await engine.transcribe(clip)).kind, 'failed');
    assert.equal(typeof (await engine.warmUp()), 'number');
  });

  test("starting removes this user's clips that a killed control plane left behind, and no others", async () => {
    const old = join(tmpdir(), `halcyonic-speech-test-old-${process.pid}`);
    const fresh = join(tmpdir(), `halcyonic-speech-test-fresh-${process.pid}`);
    for (const directory of [old, fresh]) {
      mkdirSync(directory, { recursive: true });
      writeFileSync(join(directory, 'clip.wav'), clip);
    }
    const tenMinutesAgo = new Date(Date.now() - 600_000);
    utimesSync(old, tenMinutesAgo, tenMinutesAgo);
    try {
      await WhisperEngine.open(fakeWhisper('sweep', '').config);
      assert.equal(existsSync(old), false, 'a leftover is removed');
      assert.equal(existsSync(fresh), true, 'a clip that may be in use is kept');
    } finally {
      rmSync(old, { recursive: true, force: true });
      rmSync(fresh, { recursive: true, force: true });
    }
  });

  test('a control plane that exits mid-clip kills the engine and removes the clip', async () => {
    const { config, launches } = fakeWhisper('outlived', 'setTimeout(() => {}, 30000);');
    const script = join(scratch, 'exits-mid-clip.mjs');
    const whisper = fileURLToPath(new URL('./whisper.ts', import.meta.url));
    writeFileSync(
      script,
      `import { WhisperEngine } from ${JSON.stringify(whisper)};
const engine = await WhisperEngine.open(JSON.parse(process.argv[2]));
void engine.transcribe(Buffer.from(process.argv[3], 'base64'));
setTimeout(() => process.exit(1), 800);
`,
    );
    const done = spawnSync(
      process.execPath,
      [script, JSON.stringify(config), clip.toString('base64')],
      {
        timeout: 20_000,
      },
    );
    assert.equal(done.status, 1);
    const launch = launches()[0];
    assert.ok(launch, 'the engine was launched before the exit');
    assert.equal(await alive(launch.pid), false);
    assert.equal(existsSync(dirname(launch.clip)), false);
  });

  test('warming up transcribes a second of silence without voice activity detection', async () => {
    const { config, launches } = fakeWhisper('warm', '');
    const engine = await WhisperEngine.open(config);
    assert.equal(typeof (await engine.warmUp()), 'number');
    const [launch] = launches();
    assert.ok(launch);
    assert.equal(launch.args.includes('--vad'), false);
    assert.equal(launch.clipBytes, 44 + 32_000);
    const failing = fakeWhisper('warm-fails', 'process.exit(1);');
    await assert.rejects(
      (await WhisperEngine.open(failing.config)).warmUp(),
      /stopped with an error/,
    );
  });
});
