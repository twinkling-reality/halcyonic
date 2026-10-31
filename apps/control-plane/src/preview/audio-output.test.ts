import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { audioOutputFor, installAudioOutput, outputInTree, pidTree } from './audio-output.ts';

const CLANG = '/usr/bin/clang';
const SKIP =
  process.platform !== 'darwin'
    ? 'CoreAudio is macOS only'
    : !existsSync(CLANG) && 'there is no /usr/bin/clang to build it';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-audio-output-')));
after(() => rmSync(base, { recursive: true, force: true }));

describe('a process and the ones it started, from the process table (S1)', () => {
  test('the tree holds the pid and every process under it, and nothing else', () => {
    const table = [
      '  9000     1',
      '  9001  9000',
      '  9002  9001',
      '  7000     1',
      '  9003  9000',
    ].join('\n');
    assert.deepEqual([...pidTree(table, 9000)].sort(), [9000, 9001, 9002, 9003]);
    assert.deepEqual([...pidTree(table, 9002)], [9002]);
    assert.deepEqual([...pidTree(table, 1234)], [1234]);
  });
});

/** A second of silence, as a WAV file: playing it opens an output stream that makes no sound. */
function silence(path: string): string {
  const rate = 8000;
  const data = Buffer.alloc(rate * 2 * 10);
  const header = Buffer.alloc(44);
  header.write('RIFF', 0);
  header.writeUInt32LE(36 + data.length, 4);
  header.write('WAVE', 8);
  header.write('fmt ', 12);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24);
  header.writeUInt32LE(rate * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36);
  header.writeUInt32LE(data.length, 40);
  writeFileSync(path, Buffer.concat([header, data]));
  return path;
}

describe("whether a game's browser sends sound out, read from CoreAudio (S1)", {
  skip: SKIP,
}, () => {
  const dataDir = join(base, 'data');
  let helper = '';
  before(async () => {
    await installAudioOutput({ dataDir, clang: CLANG });
    helper = audioOutputFor(dataDir) ?? '';
    assert.notEqual(helper, '');
  });

  test('it says 0 for processes with no output stream, and refuses arguments that are not pids', () => {
    assert.equal(execFileSync(helper, [String(process.pid)], { encoding: 'utf8' }), '0\n');
    for (const args of [[], ['x'], ['0'], ['-5'], ['12abc']]) {
      let code = 0;
      try {
        execFileSync(helper, args, { stdio: 'ignore' });
      } catch (error) {
        code = (error as { status: number }).status;
      }
      assert.equal(code, 64, JSON.stringify(args));
    }
  });

  test('it says 1 while a process of the tree plays to the output, silently here, and 0 once it stops', async () => {
    const player = spawn('/usr/bin/afplay', ['-v', '0', silence(join(base, 'silence.wav'))], {
      stdio: 'ignore',
    });
    const ended = new Promise((resolve) => player.once('exit', resolve));
    try {
      let seen = false;
      for (let i = 0; i < 40 && !seen; i += 1) {
        seen = (await outputInTree(helper, process.pid)) === true;
        if (!seen) await new Promise((resolve) => setTimeout(resolve, 100));
      }
      assert.ok(seen, 'the player, a child of this test, was seen sending audio out');
    } finally {
      player.kill('SIGTERM');
      await ended;
    }
    let after = true;
    for (let i = 0; i < 40 && after; i += 1) {
      after = (await outputInTree(helper, process.pid)) === true;
      if (after) await new Promise((resolve) => setTimeout(resolve, 100));
    }
    assert.equal(after, false);
  });

  test('a helper missing, or changed since it was built, is not used, and then nothing is said', async () => {
    assert.equal(audioOutputFor(join(base, 'none')), null);
    assert.equal(await outputInTree(null, process.pid), null);
  });
});
