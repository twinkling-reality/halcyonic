import assert from 'node:assert/strict';
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';

describe('adb for the device tools', () => {
  const directory = mkdtempSync(join(tmpdir(), 'halcyonic-adb-'));
  after(() => rmSync(directory, { recursive: true, force: true }));

  test('a failure names the subcommand and how it ended, never what adb printed', async () => {
    // A stand-in that, as adb does, names the headset in what it prints, then fails.
    const fake = join(directory, 'adb');
    writeFileSync(
      fake,
      '#!/bin/sh\necho "device SERIAL-XYZ said HEADSET-OUTPUT"\necho "error: device SERIAL-XYZ not found" >&2\nexit 1\n',
    );
    chmodSync(fake, 0o755);
    process.env.HALCYONIC_ADB = fake;
    const { adb } = await import('./adb.ts');
    await assert.rejects(adb('shell', 'getprop', 'ro.product.model'), (error: unknown) => {
      assert.equal((error as Error).message, 'adb shell getprop failed (exit code 1)');
      const everything = JSON.stringify(error, Object.getOwnPropertyNames(error));
      assert.ok(
        !everything.includes('SERIAL-XYZ') && !everything.includes('HEADSET-OUTPUT'),
        everything,
      );
      return true;
    });
  });
});
