import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { installLauncher, launcherFor, launcherPath, launcherProblem } from './launcher.ts';

const CLANG = '/usr/bin/clang';
const SKIP =
  process.platform !== 'darwin'
    ? 'the launcher is macOS only'
    : !existsSync(CLANG) && 'there is no /usr/bin/clang to build it';

const base = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-launcher-')));
after(() => rmSync(base, { recursive: true, force: true }));

/** Runs the launcher with `args`; resolves with its exit status and what it printed. */
function run(
  launcher: string,
  args: string[],
  stdio3 = false,
): Promise<{ code: number | null; out: string }> {
  return new Promise((resolve) => {
    const child = spawn(launcher, args, {
      stdio: ['ignore', 'pipe', 'pipe', ...(stdio3 ? ['pipe' as const] : [])],
    });
    let out = '';
    child.stdout?.on('data', (d) => {
      out += String(d);
    });
    child.stderr?.on('data', (d) => {
      out += String(d);
    });
    child.once('exit', (code, signal) =>
      resolve({ code: code ?? (signal === null ? null : -1), out }),
    );
  });
}

describe("the launcher a browser starts through, so it has no permissions of Halcyonic's app (S1)", {
  skip: SKIP,
}, () => {
  const dataDir = join(base, 'data');
  let built = '';

  test('it is built from the tracked source into the data folder, for its owner alone to run, and its hash recorded', async () => {
    assert.match(launcherProblem(dataDir) ?? '', /not set up/);
    assert.equal(launcherFor(dataDir), null);
    const sha256 = await installLauncher({ dataDir, clang: CLANG });
    built = launcherPath(dataDir);
    assert.equal(built, join(dataDir, 'bin', 'halcyonic-disclaim'));
    assert.equal(statSync(built).mode & 0o777, 0o500);
    assert.equal(statSync(join(dataDir, 'bin')).mode & 0o777, 0o700);
    assert.equal(readFileSync(`${built}.sha256`, 'utf8').trim(), sha256);
    assert.equal(launcherProblem(dataDir), null);
    assert.equal(launcherFor(dataDir), built);
  });

  test('a program started through it is responsible for itself, not for the app that started it', async () => {
    const probe = join(base, 'responsible');
    writeFileSync(
      `${probe}.c`,
      `#include <stdio.h>
#include <unistd.h>
pid_t responsibility_get_pid_responsible_for_pid(pid_t pid);
int main(void) { pid_t self = getpid(); printf("%s\\n", responsibility_get_pid_responsible_for_pid(self) == self ? "itself" : "another"); return 0; }
`,
    );
    execFileSync(CLANG, ['-o', probe, `${probe}.c`]);
    // Started plainly from this test, another process (the app that started the test) is responsible for it.
    assert.equal(execFileSync(probe, { encoding: 'utf8' }).trim(), 'another');
    assert.deepEqual(await run(built, [probe]), { code: 0, out: 'itself\n' });
    // Through Seatbelt too, as a browser starts.
    assert.deepEqual(
      await run(built, ['/usr/bin/sandbox-exec', '-p', '(version 1)(allow default)', probe]),
      {
        code: 0,
        out: 'itself\n',
      },
    );
  });

  test("it passes on the program's exit status, its fds and a TERM, and refuses a relative path or a missing program", async () => {
    assert.equal((await run(built, ['/bin/sh', '-c', 'exit 7'])).code, 7);
    assert.equal((await run(built, ['/bin/sh', '-c', 'kill -TERM $$'])).code, 128 + 15);
    assert.equal(
      (await run(built, ['/bin/sh', '-c', 'test -e /dev/fd/3 && exit 0 || exit 1'], true)).code,
      0,
    );
    assert.equal((await run(built, ['sh'])).code, 64);
    assert.equal((await run(built, [join(base, 'missing')])).code, 71);
    const child = spawn(built, ['/bin/sleep', '30'], { stdio: 'ignore' });
    await new Promise((resolve) => setTimeout(resolve, 300));
    child.kill('SIGTERM');
    const code = await new Promise((resolve) => child.once('exit', (exit) => resolve(exit)));
    assert.equal(code, 128 + 15, 'the TERM reached the program, which ended by it');
  });

  test('a launcher that changed, others can change, or is a link is not used', async () => {
    const changed = join(base, 'changed');
    await installLauncher({ dataDir: changed, clang: CLANG });
    const path = launcherPath(changed);
    chmodSync(path, 0o700);
    writeFileSync(path, Buffer.concat([readFileSync(path), Buffer.from([0])]));
    chmodSync(path, 0o500);
    assert.match(launcherProblem(changed) ?? '', /changed/);
    assert.equal(launcherFor(changed), null);

    const shared = join(base, 'shared');
    await installLauncher({ dataDir: shared, clang: CLANG });
    chmodSync(launcherPath(shared), 0o520);
    assert.match(launcherProblem(shared) ?? '', /others can change/);

    const linked = join(base, 'linked');
    await installLauncher({ dataDir: linked, clang: CLANG });
    const real = launcherPath(linked);
    const elsewhere = join(base, 'elsewhere');
    execFileSync('/bin/mv', [real, elsewhere]);
    symlinkSync(elsewhere, real);
    assert.match(launcherProblem(linked) ?? '', /not a plain file/);

    // Built again, it is replaced and recorded afresh.
    await installLauncher({ dataDir: changed, clang: CLANG });
    assert.equal(launcherProblem(changed), null);
  });
});
