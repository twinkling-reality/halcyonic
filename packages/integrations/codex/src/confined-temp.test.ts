import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, type TestContext, test } from 'node:test';
import { makeTemp, removeTemp, tempFolder, tempProfile } from './confined-temp.ts';
import { SANDBOX_EXEC } from './sandbox-profile.ts';

const MAC = { skip: process.platform !== 'darwin' && 'Seatbelt is macOS only' };

/** A scratch folder of the test's own: the confined homes with one home in it, and a folder outside. */
function scene(t: TestContext) {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'halcyonic-confined-temp-test-')));
  t.after(() => {
    execFileSync('/bin/chmod', ['-R', 'u+rwx', root]);
    rmSync(root, { recursive: true, force: true });
  });
  const homes = join(root, 'codex-confined');
  mkdirSync(join(homes, 'k'), { recursive: true, mode: 0o700 });
  const outside = join(root, 'outside');
  mkdirSync(outside);
  writeFileSync(join(outside, 'kept.txt'), 'kept\n');
  return { root, homes, outside, temp: join(homes, 'k', 'tmp') };
}

describe("a confined server's temporary folder (ADR 0041)", () => {
  test('is named as the file system spells the homes folder, never through the home', async (t) => {
    const { root, homes, outside } = scene(t);
    const alias = join(root, 'alias');
    symlinkSync(homes, alias);
    assert.equal(await tempFolder(alias, 'k'), join(homes, 'k', 'tmp'));
    symlinkSync(outside, join(homes, 'linked'));
    assert.equal(await tempFolder(homes, 'linked'), join(homes, 'linked', 'tmp'));
  });

  test('is made anew, empty and closed to others', MAC, async (t) => {
    const { temp } = scene(t);
    mkdirSync(temp, { mode: 0o755 });
    writeFileSync(join(temp, 'left.txt'), 'left\n');
    assert.equal(await makeTemp(temp), true);
    assert.deepEqual(readdirSync(temp), []);
    assert.equal(statSync(temp).mode & 0o777, 0o700);
    rmSync(temp, { recursive: true });
    assert.equal(await makeTemp(temp), true, 'made where there was none');
    assert.ok(statSync(temp).isDirectory());
  });

  test(
    'is removed with folders a command made unwritable, and a link in it removed, never followed',
    MAC,
    async (t) => {
      const { outside, temp } = scene(t);
      mkdirSync(join(temp, 'locked', 'inner'), { recursive: true });
      writeFileSync(join(temp, 'locked', 'inner', 'file'), 'file\n');
      symlinkSync(outside, join(temp, 'outside'));
      chmodSync(join(temp, 'locked', 'inner'), 0o555);
      chmodSync(join(temp, 'locked'), 0o000);
      const mode = statSync(outside).mode;
      await removeTemp(temp);
      assert.ok(!existsSync(temp));
      assert.equal(readFileSync(join(outside, 'kept.txt'), 'utf8'), 'kept\n');
      assert.equal(statSync(outside).mode, mode);
    },
  );

  test('changes nothing through a home a command replaced with a link', MAC, async (t) => {
    const { root, homes, outside } = scene(t);
    // A confined command may move its home into its project and put a link in its place.
    mkdirSync(join(outside, 'tmp', 'read-only'), { recursive: true });
    writeFileSync(join(outside, 'tmp', 'kept.txt'), 'kept\n');
    chmodSync(join(outside, 'tmp', 'read-only'), 0o555);
    renameSync(join(homes, 'k'), join(root, 'moved'));
    symlinkSync(outside, join(homes, 'k'));
    const temp = await tempFolder(homes, 'k');
    await removeTemp(temp);
    assert.equal(readFileSync(join(outside, 'tmp', 'kept.txt'), 'utf8'), 'kept\n');
    assert.equal(statSync(join(outside, 'tmp', 'read-only')).mode & 0o777, 0o555);
    assert.equal(await makeTemp(temp), false);
    // Nor is a folder made through such a link.
    const elsewhere = join(root, 'elsewhere');
    mkdirSync(elsewhere);
    symlinkSync(elsewhere, join(homes, 'other'));
    assert.equal(await makeTemp(await tempFolder(homes, 'other')), false);
    assert.deepEqual(readdirSync(elsewhere), []);
  });

  test('is made and removed inside a profile that refuses every write outside it', MAC, (t) => {
    const { outside, temp } = scene(t);
    mkdirSync(temp);
    const inside = (command: string[]) =>
      spawnSync(SANDBOX_EXEC, ['-p', tempProfile(temp), ...command], { stdio: 'ignore' }).status;
    assert.notEqual(inside(['/usr/bin/touch', join(outside, 'new')]), 0);
    assert.ok(!existsSync(join(outside, 'new')));
    assert.notEqual(inside(['/bin/rm', join(outside, 'kept.txt')]), 0);
    assert.ok(existsSync(join(outside, 'kept.txt')));
    assert.equal(inside(['/usr/bin/touch', join(temp, 'new')]), 0);
  });
});
