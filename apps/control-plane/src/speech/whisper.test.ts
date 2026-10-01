import assert from 'node:assert/strict';
import { chmodSync, existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
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
}

/**
 * A stand-in for whisper-cli: reports a version, records how it was launched and what it was
 * given, then does what `behaviour` says.
 */
function fakeWhisper(
  name: string,
  behaviour: string,
): { config: SpeechConfig; launches: () => Launch[] } {
  const record = join(scratch, `${name}.jsonl`);
  const binary = join(scratch, `${name}.cjs`);
  writeFileSync(
    binary,
    `#!${process.execPath}
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
if (args[0] === '--version') { process.stdout.write('whisper.cpp version: 1.9.4-test\\n'); process.exit(0); }
const clip = args[args.indexOf('-f') + 1];
fs.appendFileSync(${JSON.stringify(record)}, JSON.stringify({
  args, env: Object.keys(process.env), clip,
  clipMode: fs.statSync(clip).mode & 0o777,
  directoryMode: fs.statSync(path.dirname(clip)).mode & 0o777,
  clipBytes: fs.statSync(clip).size,
}) + '\\n');
${behaviour}
`,
  );
  chmodSync(binary, 0o755);
  return {
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
  test('reports its version, and a binary that does not stops startup', async () => {
    const { config } = fakeWhisper('version', '');
    assert.deepEqual((await WhisperEngine.open(config)).engine, {
      name: 'whisper.cpp',
      version: '1.9.4-test',
    });
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
