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

/**
 * How long a stand-in is given to answer --version: it is a node script, which a loaded Mac can
 * take longer than the real binary's 5 s to start, so the tests never race that bound.
 */
const VERSION_WAIT_MS = 60_000;
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
  versionDelayMs = 0,
): { config: SpeechConfig; launches: () => Launch[]; record: string } {
  const record = join(scratch, `${name}.jsonl`);
  const binary = join(scratch, `${name}.cjs`);
  writeFileSync(
    binary,
    `#!${process.execPath}
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
if (args[0] === '--version') {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ${versionDelayMs});
  process.stdout.write('whisper.cpp version: ${version}\\n');
  process.exit(0);
}
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
    assert.deepEqual(
      (await WhisperEngine.open(config, undefined, undefined, VERSION_WAIT_MS)).engine,
      {
        name: 'whisper.cpp',
        version: '1.9.4-dev',
      },
    );
    const tagged = fakeWhisper('version-tagged', '', '1.9.4');
    assert.equal(
      (await WhisperEngine.open(tagged.config, undefined, undefined, VERSION_WAIT_MS)).engine
        .version,
      '1.9.4',
    );
    for (const other of ['1.10.0', '1.9.40', '1.9.4-rc1']) {
      await assert.rejects(
        WhisperEngine.open(
          fakeWhisper(`version-${other}`, '', other).config,
          undefined,
          undefined,
          VERSION_WAIT_MS,
        ),
        /Halcyonic runs whisper\.cpp 1\.9\.4/,
        other,
      );
    }
    const silent = join(scratch, 'not-whisper.cjs');
    writeFileSync(silent, `#!${process.execPath}\nprocess.stdout.write('hello\\n');\n`);
    chmodSync(silent, 0o755);
    await assert.rejects(
      WhisperEngine.open({ ...config, binary: silent }, undefined, undefined, VERSION_WAIT_MS),
      /did not report a whisper\.cpp version/,
    );
  });

  test('waits for --version as long as it is given, and a binary slower than that stops startup', async () => {
    // Slower than the 5 s the real binary is given: a node stand-in on a loaded Mac can be.
    const slow = fakeWhisper('version-slow', '', '1.9.4', 6_000);
    assert.equal(
      (await WhisperEngine.open(slow.config, undefined, undefined, 30_000)).engine.version,
      '1.9.4',
    );
    await assert.rejects(
      WhisperEngine.open(slow.config, undefined, undefined, 500),
      /did not answer --version within 0\.5 s/,
    );
  });

  test('runs in English with the prompt and voice activity detection, with no environment, from a private file removed after', async () => {
    const { config, launches } = fakeWhisper(
      'speak',
      "process.stdout.write('\\n Use TypeScript  instead.\\n');",
    );
    const engine = await WhisperEngine.open(config, undefined, undefined, VERSION_WAIT_MS);
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
    assert.deepEqual(
      await (await WhisperEngine.open(config, undefined, undefined, VERSION_WAIT_MS)).transcribe(
        clip,
      ),
      {
        kind: 'text',
        text: '',
      },
    );
  });

  // Each fake engine is a Node process, which can take seconds to start on a loaded machine. An
  // engine that ends by itself gets more time than it could need, and one that never ends gets
  // enough to have started, so only the behaviour under test decides the outcome.
  test('an engine that fails, runs too long or writes too much gives a failure, and its clip is still removed', async () => {
    const cases = [
      ['fails', 'process.exit(3);', 30_000, 'The engine stopped with an error.'],
      ['hangs', 'setTimeout(() => {}, 60_000);', 5_000, 'The engine took longer than 5 s.'],
      [
        'floods',
        "process.stdout.write('x'.repeat(200_000));",
        30_000,
        'The engine wrote more than a transcript.',
      ],
    ] as const;
    for (const [name, behaviour, timeoutMs, message] of cases) {
      const { config, launches } = fakeWhisper(name, behaviour);
      const engine = await WhisperEngine.open(config, timeoutMs, undefined, VERSION_WAIT_MS);
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
    const engine = await WhisperEngine.open(config, 5_000, undefined, VERSION_WAIT_MS);
    const started = performance.now();
    assert.deepEqual(await engine.transcribe(clip), {
      kind: 'failed',
      message: 'The engine took longer than 5 s.',
    });
    // The grandchild sleeps for 30 s, so an answer held open by it would come far later than this.
    assert.ok(performance.now() - started < 15_000, 'not held open by what the engine started');
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
    const engine = await WhisperEngine.open(config, 300, 30_000, VERSION_WAIT_MS);
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
      await WhisperEngine.open(
        fakeWhisper('sweep', '').config,
        undefined,
        undefined,
        VERSION_WAIT_MS,
      );
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
const engine = await WhisperEngine.open(JSON.parse(process.argv[2]), undefined, undefined, 60_000);
void engine.transcribe(Buffer.from(process.argv[3], 'base64'));
setTimeout(() => process.exit(1), 5000);
`,
    );
    // Five seconds is long enough for the engine to have started even on a loaded machine.
    const done = spawnSync(
      process.execPath,
      [script, JSON.stringify(config), clip.toString('base64')],
      {
        timeout: 30_000,
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
    const engine = await WhisperEngine.open(config, undefined, undefined, VERSION_WAIT_MS);
    assert.equal(typeof (await engine.warmUp()), 'number');
    const [launch] = launches();
    assert.ok(launch);
    assert.equal(launch.args.includes('--vad'), false);
    assert.equal(launch.clipBytes, 44 + 32_000);
    const failing = fakeWhisper('warm-fails', 'process.exit(1);');
    await assert.rejects(
      (await WhisperEngine.open(failing.config, undefined, undefined, VERSION_WAIT_MS)).warmUp(),
      /stopped with an error/,
    );
  });
});
